// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Threading.Channels;

using Bevo.ReverseProxy.Kube;

using k8s;

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
            builder.Services.AddSingleton<IControllerConfiguration, ControllerConfiguration>();
            builder.Services.AddSingleton<IBindingPortManagement, KubernetesBindingPortManagement>();

            builder.Services.AddSingleton<IKubernetes>(sp =>
            {
                var client = new Kubernetes(LocateKubeConfig());
                client.SerializationSettings.Converters.Add(new JsonDateTimeConverter());
                return client;
            });

            builder.Services.AddSingleton<IKubeResourceStore, KubeResourceStore>();

            builder.Services.AddSingleton<IEventRecorder, EventRecorder>();
            builder.Services.AddSingleton<Channel<KubeEvent>>(Channel.CreateBounded<KubeEvent>(500));
            builder.Services.AddHostedService<EventBroadcaster>();
            builder.Services.AddHostedService<StatusReporterService>();
        }

        private static KubernetesClientConfiguration LocateKubeConfig()
        {
            // Attempt to dynamically determine between in-cluster and host (debug) development...
            var serviceHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            var servicePort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");

            // Locate from environment variables directly. Unlike IConfiguration, which could be overridden.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POD_NAME")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POD_NAMESPACE")))
            {
                throw new InvalidOperationException("Failed to detect current pod information. Check POD_NAME and POD_NAMESPACE environment variables.");
            }

            if (!string.IsNullOrWhiteSpace(serviceHost) && !string.IsNullOrWhiteSpace(servicePort))
            {
                return KubernetesClientConfiguration.InClusterConfig();
            }

            return KubernetesClientConfiguration.BuildConfigFromConfigFile();
        }
    }
}
