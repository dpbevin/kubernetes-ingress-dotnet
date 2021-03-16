// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube
{
    // Credit to https://gist.github.com/cocowalla/5d181b82b9a986c6761585000901d1b8
    public class Debouncer : IDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly TimeSpan waitTime;
        private int counter;

        public Debouncer(TimeSpan? waitTime = null)
        {
            this.waitTime = waitTime ?? TimeSpan.FromSeconds(3);
        }

        public void Debounce(Action action)
        {
            var current = Interlocked.Increment(ref this.counter);

            Task.Delay(this.waitTime).ContinueWith(task =>
            {
                // Is this the last task that was queued?
                if (current == this.counter && !this.cts.IsCancellationRequested)
                    action();

                task.Dispose();
            }, this.cts.Token);
        }

        public void Dispose()
        {
            this.cts.Cancel();
        }
    }
}
