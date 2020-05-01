// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------

namespace ActorClient
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Dapr.Actors;
    using Dapr.Actors.Client;
    using IDemoActorInterface;

    /// <summary>
    /// Actor Client class.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Entry point.
        /// before running this, the actor must also be running
        /// dapr run --port 3500 --app-id appTestActorMiddleware --app-port 5000 dotnet run
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task Main(string[] args)
        {
            var data = new MyData()
            {
                PropertyA = "ValueA",
                PropertyB = "ValueB",
            };


            // Create an actor Id.
            var actorId = new ActorId("abc");

            // Make strongly typed Actor calls with Remoting.
            // DemoActor is the type registered with Dapr runtime in the service.
            var proxy = ActorProxy.Create<IDemoActor>(actorId, "DemoActor");

            Console.WriteLine("Making call using actor proxy to save data.");
            await proxy.SaveData(data);
            Console.WriteLine("Making call using actor proxy to get data.");
            var receivedData = await proxy.GetData();
            Console.WriteLine($"Received data is {receivedData}.");

            //making the same call via externall HTTP call to the actor
            HttpClient httpClient = new HttpClient();

            string formURLEncodedContent = "PropertyA=ValueA&PropertyB=ValueB";
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };

            //test to show that middle ware is active
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/AppPortEndPoint")
            {
                Content = new StringContent("", Encoding.UTF8)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string JsonPayload = "{\"PropertyA\":\"ValueA\",\"PropertyB\":\"ValueB\"}";
            request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:3500/v1.0/actors/DemoActor/123/method/SaveData")
            {
                Content = new StringContent(JsonPayload, Encoding.UTF8)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

             response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:3500/v1.0/actors/DemoActor/123/method/SaveData")
            {
                Content = new StringContent(formURLEncodedContent, Encoding.UTF8)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            try
            // Making some more calls to test methods.
            {
                Console.WriteLine("Making calls to an actor method which has no argument and no return type.");
                await proxy.TestNoArgumentNoReturnType();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Got exception while making call to method with No Argument & No Return Type. Exception: {ex}");
            }

            try
            {
                await proxy.TestThrowException();
            }
            catch (ActorMethodInvocationException ex)
            {
                if (ex.InnerException is NotImplementedException)
                {
                    Console.WriteLine($"Got Correct Exception from actor method invocation.");
                }
                else
                {
                    Console.WriteLine($"Got Incorrect Exception from actor method invocation. Exception {ex.InnerException}");
                }
            }

            // Making calls without Remoting, this shows method invocation using InvokeAsync methods, the method name and its payload is provided as arguments to InvokeAsync methods.
            Console.WriteLine("Making calls without Remoting.");
            var nonRemotingProxy = ActorProxy.Create(actorId, "DemoActor");
            await nonRemotingProxy.InvokeAsync("TestNoArgumentNoReturnType");
            await nonRemotingProxy.InvokeAsync("SaveData", data);
            var res = await nonRemotingProxy.InvokeAsync<MyData>("GetData");

            Console.WriteLine("Registering the timer and reminder");
            await proxy.RegisterTimer();
            await proxy.RegisterReminder();
            Console.WriteLine("Waiting so the timer and reminder can be triggered");
            await Task.Delay(6000);

            Console.WriteLine("Making call using actor proxy to get data after timer and reminder triggered");
            receivedData = await proxy.GetData();
            Console.WriteLine($"Received data is {receivedData}.");

            Console.WriteLine("Deregistering timer. Timers would any way stop if the actor is deactivated as part of Dapr garbage collection.");
            await proxy.UnregisterTimer();
            Console.WriteLine("Deregistering reminder. Reminders are durable and would not stop until an explicit deregistration or the actor is deleted.");
            await proxy.UnregisterReminder();
        }
    }
}
