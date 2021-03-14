// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;

namespace Bevo.ReverseProxy.Kube
{
    public interface IKubeResourceStore
    {
        IEnumerable<IngressModel> Ingresses { get; }

        string GetBackendConfiguration();
    }
}
