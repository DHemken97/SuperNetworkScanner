using SuperNetworkScanner.Extensions;
using SuperNetworkScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkInterface = SuperNetworkScanner.Models.NetworkInterface;

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

        private int TotalHosts = 0;
        private int _completed = 0;

        private readonly ConcurrentQueue<string> _progressLog = new();
        private readonly ConcurrentBag<Host> _foundHosts = new();

        public void Start(List<string> search_ips)
        {
            IsCompleted = false;
            _completed = 0;
            TotalHosts = search_ips.Count;

            Parallel.ForEachAsync(search_ips, new ParallelOptions { MaxDegreeOfParallelism = 100 }, async (ip, ct) =>
            {
                await PingIp(ip);
                Interlocked.Increment(ref _completed);
            }).ContinueWith(_ =>
            {
                lock (NetworkMap.Hosts)
                {
                    foreach (var foundHost in _foundHosts)
                    {
                        var ip = foundHost.NetworkInterfaces.First().Ip_Address.First();
                        var existingHost = NetworkMap.Hosts
                            .FirstOrDefault(h => h.NetworkInterfaces
                            .Any(ni => ni.Ip_Address.Contains(ip)));

                        if (existingHost != null)
                        {
                            var ni = existingHost.NetworkInterfaces.FirstOrDefault();
                            if (ni != null && !ni.Ip_Address.Contains(ip))
                                ni.Ip_Address.Add(ip);

                            existingHost.Status = HostStatus.Online;
                        }
                        else
                        {
                            foundHost.Status = HostStatus.Online;
                            NetworkMap.Hosts.Add(foundHost);
                        }
                    }
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
                    _foundHosts.Add(new Host
                    {
                        NetworkInterfaces = new List<NetworkInterface>
                        {
                            new NetworkInterface
                            {
                                Ip_Address = new List<string> { ip }
                            }
                        },
                        Status = HostStatus.Online
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
