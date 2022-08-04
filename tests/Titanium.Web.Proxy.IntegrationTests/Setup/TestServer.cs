﻿using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Titanium.Web.Proxy.IntegrationTests.Setup
{
    // set up a kestrel test server
    public class TestServer : IDisposable
    {
        public string ListeningHttpUrl => $"http://localhost:{HttpListeningPort}";
        public string ListeningHttpsUrl => $"https://localhost:{HttpsListeningPort}";
        public string ListeningTcpUrl => $"http://localhost:{TcpListeningPort}";

        public int HttpListeningPort { get; private set; }
        public int HttpsListeningPort { get; private set; }
        public int TcpListeningPort { get; private set; }

        private readonly IHost host;
        public TestServer(X509Certificate2 serverCertificate, bool requireMutualTls)
        {
            host = Host.CreateDefaultBuilder()
                         .ConfigureLogging(logging =>
                         {
                             logging.ClearProviders();
                             logging.AddDebug();
                             logging.SetMinimumLevel(LogLevel.Trace);
                         })
                         .ConfigureWebHostDefaults(webBuilder =>
                         {
                             webBuilder.UseStartup(x => new Startup(() => requestHandler));
                             webBuilder.ConfigureKestrel(options =>
                             {
                                 options.Listen(IPAddress.Loopback, 0);
                                 if (requireMutualTls)
                                 {
                                     options.ConfigureHttpsDefaults(options =>
                                     {
                                         options.ClientCertificateValidation = (certificate, chain, errors) =>
                                         {
                                             return true;
                                         };
                                         options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                     });
                                 }
                                 options.Listen(IPAddress.Loopback, 0, listenOptions =>
                                 {
                                     listenOptions.UseHttps(serverCertificate);
                                 });
                                 options.Listen(IPAddress.Loopback, 0, listenOptions =>
                                 {
                                     listenOptions.Run(context =>
                                     {
                                         if (tcpRequestHandler == null)
                                         {
                                             throw new Exception("Test server not configured to handle tcp request.");
                                         }

                                         return tcpRequestHandler(context);
                                     });
                                 });
                             });
                         })
                        .Build();

            host.Start();

            var addresses = host.Services.GetRequiredService<IServer>()
                         .Features.Get<IServerAddressesFeature>()
                        .Addresses.ToArray();

            HttpListeningPort = new Uri(addresses[0]).Port;
            HttpsListeningPort = new Uri(addresses[1]).Port;
            TcpListeningPort = new Uri(addresses[2]).Port;
        }

        Func<HttpContext, Task> requestHandler = null;
        Func<ConnectionContext, Task> tcpRequestHandler = null;

        public void HandleRequest(Func<HttpContext, Task> requestHandler)
        {
            this.requestHandler = requestHandler;
        }

        public void HandleTcpRequest(Func<ConnectionContext, Task> tcpRequestHandler)
        {
            this.tcpRequestHandler = tcpRequestHandler;
        }

        public void Dispose()
        {
            host.StopAsync().Wait();
            host.Dispose();
        }

        private class Startup
        {
            Func<Func<HttpContext, Task>> requestHandler;
            public Startup(Func<Func<HttpContext, Task>> requestHandler)
            {
                this.requestHandler = requestHandler;
            }

            public void Configure(IApplicationBuilder app)
            {
                app.Run(context =>
                {
                    if (requestHandler == null)
                    {
                        throw new Exception("Test server not configured to handle request.");
                    }

                    return requestHandler()(context);
                });

            }

            public void ConfigureServices(IServiceCollection services)
            {

            }
        }
    }
}