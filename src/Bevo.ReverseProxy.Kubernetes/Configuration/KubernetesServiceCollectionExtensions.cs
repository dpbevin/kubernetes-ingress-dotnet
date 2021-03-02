// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using Bevo.ReverseProxy.Kube;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.Service;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class KubernetesServiceCollectionExtensions
    {
        public static IReverseProxyBuilder LoadFromKubernetes(this IReverseProxyBuilder builder, IConfiguration configuration)
        {
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));

            AddServices(builder);

            builder.Services.Configure<KubernetesDiscoveryOptions>(configuration);

            return builder;
        }

        private static void AddServices(IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IKubernetesDiscoverer, KubernetesDiscoverer>();
            builder.Services.AddSingleton<IProxyConfigProvider, KubernetesConfigProvider>();
        }
    }
}
