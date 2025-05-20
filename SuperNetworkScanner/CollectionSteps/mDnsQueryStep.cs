using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuperNetworkScanner.Models;
using Zeroconf; // IMPORTANT: Add this using directive

namespace SuperNetworkScanner.CollectionSteps
{
    public class MDNSQueryStep : ICollectionStep
    {
        public string Name => "mDNS Sweep";

        public string Description => "Discovers hostnames and services on the local network using mDNS.";

        public string ProgressMessage { get; private set; }

        public string ProgressLog { get; private set; } = string.Empty;

        public decimal ProgressPercentage { get; private set; }

        public bool IsCompleted { get; private set; }

        private readonly object _logLock = new();
        private readonly object _progressLock = new();

        public async void Start(List<string> search_ips)
        {
            // For mDNS, the search_ips list is less about targetting specific IPs
            // and more about providing a context or a "pool" of IPs that might exist
            // on the network. Zeroconf will broadcast to the local network.
            // We'll use the count of search_ips as a rough total for progress,
            // or if you intend to do targeted mDNS (less common, usually just listen).

            int totalExpectedHosts = search_ips.Count > 0 ? search_ips.Count : 100; // Assume a reasonable max if no IPs provided
            int discoveredCount = 0;
            // Use a concurrent dictionary to avoid duplicate processing of the same IP if multiple mDNS packets arrive
            var processedIps = new ConcurrentDictionary<string, bool>();

            try
            {
                ProgressMessage = "Starting mDNS discovery (listening for 5 seconds)...";
                ProgressLog += "Initiating mDNS discovery...\r\n";

                // ResolveAsync listens for mDNS responses for a specified duration.
                // TimeSpan.FromSeconds(5) is a common duration for initial sweeps.
                // Adjust this value based on your network and desired thoroughness.
                IReadOnlyList<IZeroconfHost> hosts = await ZeroconfResolver.ResolveAsync(protocol: "_services._dns-sd._udp.local", scanTime: TimeSpan.FromSeconds(5), retries: 3, retryDelayMilliseconds: 1000);
                ProgressLog += $"mDNS discovery completed. Found {hosts.Count} potential hosts.\r\n";

                foreach (var hostResolved in hosts)
                {
                    string ipAddress = hostResolved.IPAddress;
                    string hostname = hostResolved.DisplayName; // Often the .local hostname
                    List<string> services = hostResolved.Services.Select(s => s.Key).ToList();

                    if (string.IsNullOrWhiteSpace(ipAddress) || processedIps.ContainsKey(ipAddress))
                    {
                        // Skip if no IP or already processed to avoid redundant updates
                        continue;
                    }

                    processedIps.TryAdd(ipAddress, true); // Mark this IP as processed

                    string result = $"mDNS found for {ipAddress}: Hostname='{hostname}'";

                    // Try to find an existing host in NetworkMap based on IP
                    var existingHost = NetworkMap.Hosts
                        .FirstOrDefault(h => h.NetworkInterfaces
                        .Any(ni => ni.Ip_Address.Contains(ipAddress)));

                    if (existingHost != null)
                    {
                        // Update existing host's hostname if better or add services
                        if (string.IsNullOrWhiteSpace(existingHost.Hostname) || existingHost.Hostname.Contains("?"))
                        {
                            existingHost.Hostname = hostname;
                            result += " - Updated existing host hostname.";
                        }
                        else
                        {
                            result += " - Hostname already known, skipping update.";
                        }

                        // Add new services if they don't already exist for this host
                      /*  foreach (var service in services)
                        {
                            if (!existingHost.DiscoveredServices.Contains(service))
                            {
                                existingHost.DiscoveredServices.Add(service);
                                result += $" Added service: {service}.";
                            }
                        }*/
                    }
                    else
                    {
                        // Create a new host entry if not found
                        var newHost = new Host
                        {
                            Hostname = hostname,
                            NetworkInterfaces = new List<NetworkInterface> { new NetworkInterface { Ip_Address = new List<string> { ipAddress } } },
                           // DiscoveredServices = services
                        };
                        NetworkMap.Hosts.Add(newHost);
                        result += " - Added as a new host.";
                    }

                    lock (_logLock)
                    {
                        ProgressLog += result + "\r\n";
                    }

                    lock (_progressLock)
                    {
                        discoveredCount++;
                        // Progress calculation for mDNS is a bit tricky as total isn't fixed.
                        // We can either set it to a fixed time-based progress or
                        // calculate based on discovered vs. expected (if search_ips gives a good hint)
                        ProgressPercentage = Math.Min(1.0m, (decimal)discoveredCount / totalExpectedHosts);
                        ProgressMessage = $"Discovered {discoveredCount} mDNS hosts. Listening...";
                    }
                }

                if (!hosts.Any())
                {
                    ProgressLog += "No mDNS hosts responded during the sweep.\r\n";
                }
            }
            catch (Exception ex)
            {
                ProgressLog += $"mDNS discovery failed: {ex.Message}\r\n";
                ProgressMessage = "mDNS discovery failed.";
            }
            finally
            {
                IsCompleted = true;
                ProgressMessage = "mDNS sweep completed.";
                ProgressPercentage = 1.0m; // Ensure 100% on completion
                ProgressLog += "mDNS sweep finished.\r\n";
            }
        }
    }
}