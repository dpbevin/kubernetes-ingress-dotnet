using Bevo.ReverseProxy.Kube.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class KubernetesServiceCollectionExtensions
    {
        public static IReverseProxyBuilder LoadFromKubernetes(this IReverseProxyBuilder builder, IConfiguration configuration)
        {
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));

            AddServices(builder);

            //builder.Services.Configure<ServiceFabricDiscoveryOptions>(configuration);

            return builder;
        }

        private static void AddServices(IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IKubernetesDiscoverer, KubernetesDiscoverer>();
            builder.Services.AddSingleton<IProxyConfigProvider, KubernetesConfigProvider>();
        }
    }
}
