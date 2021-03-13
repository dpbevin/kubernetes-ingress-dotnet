// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;

namespace Bevo.ReverseProxy.Kube
{
    public class ControllerConfiguration : IControllerConfiguration
    {
        public string PodNamespace => Environment.GetEnvironmentVariable("POD_NAMESPACE");

        public string PodName => Environment.GetEnvironmentVariable("POD_NAME");

        public string PublishService => Environment.GetEnvironmentVariable("PUBLISH_SERVICE");
    }
}
