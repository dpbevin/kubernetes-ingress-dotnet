using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    // When we get to TLS certs for ingresses... this will be very handy!
                    // webBuilder.ConfigureKestrel(configureOptions =>
                    // {
                    //     configureOptions.ListenAnyIP(5001, listenOptions =>
                    //     {
                    //         listenOptions.UseHttps(httpsOptions =>
                    //         {
                    //             httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                    //             {
                    //                 // Remember to include a default cert!
                    //                 CustomCertificateProvider myCertProvider = listenOptions.ApplicationServices.GetService(typeof(CustomCertificateProvider));
                    //                 return myCertProvider.GetBindingCertificate(name);
                    //             };
                    //         });
                    //     });
                    // });
                });
    }
}
