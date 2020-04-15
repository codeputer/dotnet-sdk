﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------

namespace RoutingSample
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Dapr;
    using Dapr.Client;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Twilio.AspNet.Common;
    using Twilio.AspNet.Core;
    using Twilio.TwiML;

    /// <summary>
    /// Startup class.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// State store name.
        /// </summary>
        public const string StoreName = "statestore";

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration">Configuration.</param>
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configures Services.
        /// </summary>
        /// <param name="services">Service Collection.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDaprClient();

            services.AddSingleton(new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            services.AddControllers();
        }

        /// <summary>
        /// Configures Application Builder and WebHost environment.
        /// </summary>
        /// <param name="app">Application builder.</param>
        /// <param name="env">Webhost environment.</param>
        /// <param name="serializerOptions">Options for JSON serialization.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, JsonSerializerOptions serializerOptions)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();


            app.UseCloudEvents();
            app.DecodeFormURLToJson("TwilioPost");
            //app.UseActors();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapSubscribeHandler();

                endpoints.MapGet("{id}", Balance);
                endpoints.MapPost("deposit", Deposit).WithTopic("deposit");
                endpoints.MapPost("withdraw", Withdraw).WithTopic("withdraw");
                endpoints.MapPost("twiliopostinjson", twiliopostinjson);

                endpoints.MapControllers();
            });

            async Task twiliopostinjson(HttpContext httpContext)
            {
                var voiceRequest = await JsonSerializer.DeserializeAsync<VoiceRequest>(httpContext.Request.Body);

                //put call on hold
                //pause this call... which will continue the ringback until we handle it via the event and modify a call in progress
                var voiceResponse = new VoiceResponse().Pause(Convert.ToInt32(TimeSpan.FromMinutes(3).TotalSeconds)); //todo: PauseLength should be configured}}

                var callIdentifier = voiceRequest.CallSid;
                var fromNumber = voiceRequest.To;
                //instanciate actor and pass control to them using Actor ID CallSID
                //callsid must then send message to PhoneActorNumber from CallSidActor.... after it reminds itself and wakes up

                if (voiceResponse == null)
                {
                    httpContext.Response.StatusCode = 500;
                }
                else
                {
                    httpContext.Response.ContentType = "application/xml";
                    await httpContext.Response.WriteAsync(voiceResponse.ToString());
                    System.Diagnostics.Debug.WriteLine(voiceResponse.ToString());
                }
            }

            async Task Balance(HttpContext context)
            {
                var client = context.RequestServices.GetRequiredService<DaprClient>();

                var id = (string)context.Request.RouteValues["id"];
                var account = await client.GetStateAsync<Account>(StoreName, id);
                if (account == null)
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, account, serializerOptions);
            }

            async Task Deposit(HttpContext context)
            {
                var client = context.RequestServices.GetRequiredService<DaprClient>();

                var transaction = await JsonSerializer.DeserializeAsync<Transaction>(context.Request.Body, serializerOptions);
                System.Diagnostics.Debug.Print("after deserialization");

                var account = await client.GetStateAsync<Account>(StoreName, transaction.Id);
                if (account == null)
                {
                    account = new Account() { Id = transaction.Id, };
                }

                if (transaction.Amount < 0m)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                account.Balance += transaction.Amount;
                await client.SaveStateAsync(StoreName, transaction.Id, account);

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, account, serializerOptions);
            }

            async Task Withdraw(HttpContext context)
            {
                var client = context.RequestServices.GetRequiredService<DaprClient>();

                var transaction = await JsonSerializer.DeserializeAsync<Transaction>(context.Request.Body, serializerOptions);
                var account = await client.GetStateAsync<Account>(StoreName, transaction.Id);
                if (account == null)
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                if (transaction.Amount < 0m)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                account.Balance -= transaction.Amount;
                await client.SaveStateAsync(StoreName, transaction.Id, account);

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, account, serializerOptions);
            }
        }


        //public static MemoryStream GenerateStreamFromString(string value)
        //{
        //    return new MemoryStream(Encoding.UTF8.GetBytes(value ?? ""));
        //}
    }

}
