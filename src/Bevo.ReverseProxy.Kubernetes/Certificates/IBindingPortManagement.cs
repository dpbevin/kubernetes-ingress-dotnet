// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;

namespace Bevo.ReverseProxy.Kube
{
    public interface IBindingPortManagement
    {
        int TlsPort { get; }

        int HttpPort { get; }

        bool EnableTls { get; }

        X509Certificate2 GetCertificate(ConnectionContext context, string name);
    }
}
