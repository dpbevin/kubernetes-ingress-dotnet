// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

namespace Bevo.ReverseProxy.Kube
{
    public interface IControllerConfiguration
    {
        string PodNamespace { get; }

        string PodName { get; }

        string PublishService { get; }
    }
}
