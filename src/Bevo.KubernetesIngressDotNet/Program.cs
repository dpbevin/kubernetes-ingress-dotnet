// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using Bevo.ReverseProxy.Kube;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KubernetesIngressDotNet
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.AddSystemdConsole();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    // When we get to TLS certs for ingresses... this will be very handy!
                    webBuilder.ConfigureKestrel(configureOptions =>
                    {
                        var portManagement = configureOptions.ApplicationServices.GetService(typeof(IBindingPortManagement)) as IBindingPortManagement;
                        configureOptions.ListenAnyIP(portManagement.HttpPort);

                        if (portManagement.EnableTls)
                        {
                            configureOptions.ListenAnyIP(portManagement.TlsPort, listenOptions =>
                            {
                                listenOptions.UseHttps(httpsOptions =>
                                {
                                    httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate;
                                    httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                                    {
                                        return portManagement.GetCertificate(connectionContext, name);
                                    };
                                });
                            });
                        }
                    });
                });
    }
}
