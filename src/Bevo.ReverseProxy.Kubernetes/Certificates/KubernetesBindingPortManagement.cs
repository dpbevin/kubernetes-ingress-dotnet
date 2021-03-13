// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

using k8s;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bevo.ReverseProxy.Kube
{
    public class KubernetesBindingPortManagement : IBindingPortManagement
    {
        private readonly IKubernetes _client;
        private readonly IOptionsMonitor<KubernetesDiscoveryOptions> _optionsMonitor;
        private readonly ILogger _logger;

        public KubernetesBindingPortManagement(IKubernetes client, IOptionsMonitor<KubernetesDiscoveryOptions> optionsMonitor, ILogger<KubernetesBindingPortManagement> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public int HttpPort => _optionsMonitor.CurrentValue.HttpPort;

        public bool EnableTls => _optionsMonitor.CurrentValue.EnableTls;

        public int TlsPort => _optionsMonitor.CurrentValue.TlsPort;

        public X509Certificate2 GetCertificate(ConnectionContext context, string name)
        {
            _logger.LogDebug($"Loading certificate for {name}");

            var tlsSecret = _client.ListNamespacedSecret("dev", fieldSelector: "metadata.name=ingress-dotnet-tls").Items.FirstOrDefault();
            if (tlsSecret != null)
            {
                var crt = tlsSecret.Data["tls.crt"].Select(c => (char)c).ToArray();
                var key = tlsSecret.Data["tls.key"].Select(c => (char)c).ToArray();

                var cert = X509Certificate2.CreateFromPem(crt, key);

                // Cert needs converting. Read https://github.com/dotnet/runtime/issues/23749#issuecomment-388231655
                return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            }

            return null;
        }
    }
}
