using SuperNetworkScanner.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuperNetworkScanner.CollectionSteps
{
    public class PingSweepStep : ICollectionStep
    {
        public string Name => "Ping Sweep";

        public string Description => "Ping a range of IPs to see what responds";

        public string ProgressMessage { get; private set; }

        public string ProgressLog => string.Join("\r\n", _progressLog.Reverse());

        public decimal ProgressPercentage => TotalHosts == 0 ? 0 : (decimal)_completed / TotalHosts;

        public bool IsCompleted { get; private set; }

        private const int TotalHosts = 254;
        private int _completed = 0;

        private ConcurrentQueue<string> _progressLog = new();
        private ConcurrentBag<Models.Host> _foundHosts = new();

        public void Start()
        {
            IsCompleted = false;
            _completed = 0;

            var ips = Enumerable.Range(1, TotalHosts).Select(i => $"192.168.12.{i}");

            Parallel.ForEachAsync(ips, new ParallelOptions { MaxDegreeOfParallelism = 100 }, async (ip, ct) =>
            {
                await PingIp(ip);
                Interlocked.Increment(ref _completed);
            }).ContinueWith(_ =>
            {
                lock (NetworkMap.Hosts)
                {
                    NetworkMap.Hosts.AddRange(_foundHosts);
                }
                IsCompleted = true;
            });
        }

        private async Task PingIp(string ip)
        {
            var message = $"Pinging {ip}...";
            _progressLog.Enqueue(message);

            using Ping ping = new();
            try
            {
                var reply = await ping.SendPingAsync(ip, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    _progressLog.Enqueue($"{message} OK");
                    _foundHosts.Add(new Models.Host()
                    {
                        NetworkInterfaces = new List<Models.NetworkInterface>
                        {
                            new Models.NetworkInterface { Ip_Address = new List<string> { ip } }
                        }
                    });
                }
                else
                {
                    _progressLog.Enqueue($"{message} No response");
                }
            }
            catch
            {
                _progressLog.Enqueue($"{message} Error");
            }
        }
    }
}
