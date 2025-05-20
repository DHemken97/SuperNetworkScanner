using SuperNetworkScanner.Extensions;
using SuperNetworkScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkInterface = SuperNetworkScanner.Models.NetworkInterface; // Alias for clarity

namespace SuperNetworkScanner.CollectionSteps
{
    public class PortScanStep : ICollectionStep
    {
        public string Name => "Port Scan Sweep";
        public string Description => "Port scan a range of ports to discover services.";
        public string ProgressMessage { get; private set; }
        public string ProgressLog => string.Join("\r\n", _progressLog.Reverse());
        public decimal ProgressPercentage => TotalHosts == 0 ? 0 : (decimal)_completed / TotalHosts;
        public List<int> Ports { get; set; } = new List<int>();
        public bool IsCompleted { get; private set; }

        private int TotalHosts = 0;
        private int _completed = 0;

        private readonly ConcurrentQueue<string> _progressLog = new ConcurrentQueue<string>();

        // This will be used to report found hosts/services in real-time
        // You'll need to subscribe to this event from your UI/display logic
        public event Action<Host> HostFoundOrUpdated;

        public void Start(List<string> search_ips)
        {
            IsCompleted = false;
            _completed = 0;
            TotalHosts = search_ips.Count;

            if (!Ports.Any())
            {
                _progressLog.Enqueue("Error: No ports specified for scanning.");
                IsCompleted = true;
                return;
            }

            ProgressMessage = "Starting port scan...";
            _progressLog.Enqueue("Port scan initiated.");

            // Use Task.Run to offload the Parallel.ForEachAsync from the calling thread
            // This ensures that the Start method returns quickly and doesn't block
            Task.Run(async () =>
            {
                await Parallel.ForEachAsync(search_ips, new ParallelOptions { MaxDegreeOfParallelism = 100 }, async (ip, ct) =>
                {
                    // Create a Host object for the current IP
                    var currentHost = new Host
                    {
                        NetworkInterfaces = new List<NetworkInterface>
                        {
                            new NetworkInterface
                            {
                                Ip_Address = new List<string> { ip },
                                Services = new List<Service>()
                            }
                        },
                        Status = HostStatus.Offline // Assume offline until a service is found
                    };

                    await ScanIpPorts(currentHost, ct); // Pass the host and CancellationToken

                    // Only add to NetworkMap.Hosts if services were found for this IP
                    if (currentHost.NetworkInterfaces.First().Services.Any())
                    {
                        UpdateNetworkMapHost(currentHost);
                    }
                    else
                    {
                        _progressLog.Enqueue($"{ip}: No open services found.");
                    }

                    Interlocked.Increment(ref _completed);
                    ProgressMessage = $"Scanned {_completed} of {TotalHosts} IPs.";
                });

                // This block executes only after all Parallel.ForEachAsync tasks are completed
                IsCompleted = true;
                ProgressMessage = "Port scan completed.";
                _progressLog.Enqueue("Port scan finished.");
            });
        }

        private async Task ScanIpPorts(Host host, CancellationToken ct)
        {
            var ip = host.NetworkInterfaces.First().Ip_Address.First();
            var inet = host.NetworkInterfaces.First(); // Get the NetworkInterface for the current IP

            _progressLog.Enqueue($"Scanning {ip} for open ports...");

            foreach (var port in Ports)
            {
                if (ct.IsCancellationRequested) // Check for cancellation request
                {
                    _progressLog.Enqueue($"Scan for {ip} cancelled.");
                    break;
                }

                using var tcpClient = new TcpClient();
                tcpClient.ReceiveBufferSize = 4096;
                tcpClient.SendTimeout = 2000; // Set send timeout
                tcpClient.ReceiveTimeout = 2000; // Set receive timeout for stream operations

                try
                {
                    var connectTask = tcpClient.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(2000, ct); // Use cancellation token for timeout

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                    if (completedTask == connectTask && tcpClient.Connected)
                    {
                        _progressLog.Enqueue($"  {ip}:{port} is OPEN");

                        string serviceName = KnownPortServices.GetValueOrDefault(port, $"Port {port}");
                        string banner = await GetServiceBanner(tcpClient, ct); // Pass CancellationToken to banner grabber
                        var desc = banner.StartsWith("Error") ? await GetHttpResponseHeaders(tcpClient, ip,port,ct) : banner;

                        inet.Services.Add(new Service
                        {
                            Port = port,
                            Protocol = "tcp",
                            ServiceName = serviceName,
                            Description = desc,
                        });

                        host.Status = HostStatus.Online; // Mark host as online if at least one port is open
                        HostFoundOrUpdated?.Invoke(host); // **Real-time update**
                    }
                    else
                    {
                        _progressLog.Enqueue($"  {ip}:{port} is CLOSED or TIMED OUT");
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    _progressLog.Enqueue($"  {ip}:{port} Connection refused (CLOSED)");
                }
                catch (OperationCanceledException) // Handle task cancellation
                {
                    _progressLog.Enqueue($"  {ip}:{port} operation cancelled.");
                    break; // Exit foreach loop for current IP
                }
                catch (Exception ex)
                {
                    _progressLog.Enqueue($"  {ip}:{port} Error during scan: {ex.Message}");
                }
                finally
                {
                    tcpClient.Close();
                }
            }
        }

        /// <summary>
        /// Attempts to read the initial banner from a connected TCP client.
        /// </summary>
        /// <param name="tcpClient">The connected TcpClient.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>The received banner string, or an empty string if none is read or an error occurs.</returns>
        private async Task<string> GetServiceBanner(TcpClient tcpClient, CancellationToken ct)
        {
            try
            {
                if (!tcpClient.Connected) return string.Empty;

                NetworkStream stream = tcpClient.GetStream();
                byte[] buffer = new byte[tcpClient.ReceiveBufferSize];

                // Stream ReadTimeout is set on the TcpClient.ReceiveTimeout property.
                // We're already using Task.Delay with a CancellationToken for overall connection timeout.

                int bytesRead = 0;
                try
                {
                    // Use ReadAsync with cancellation token for better control
                    var readTask = stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    var timeoutTask = Task.Delay(2000, ct); // A separate timeout for banner reading

                    var completedTask = await Task.WhenAny(readTask, timeoutTask);

                    if (completedTask == readTask)
                    {
                        bytesRead = await readTask;
                    }
                    else
                    {
                        // Timeout during banner reading
                        return "Error: Timeout reading banner.";
                    }
                }
                catch (OperationCanceledException)
                {
                    return "Banner read cancelled.";
                }

                if (bytesRead > 0)
                {
                    string banner = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    banner = System.Text.RegularExpressions.Regex.Replace(banner, @"\s+", " ").Trim();
                    banner = banner.Replace("\r", "").Replace("\n", "");
                    return banner;
                }
            }
            catch (Exception ex)
            {
                return $"Error reading banner: {ex.Message}";
            }
            return string.Empty;
        }
        /// <summary>
        /// Attempts to send an HTTP GET request and read response headers.
        /// </summary>
        /// <param name="tcpClient">The connected TcpClient.</param>
        /// <param name="ip">The IP address of the target.</param>
        /// <param name="port">The port of the target (80 for HTTP, 443 for HTTPS).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A string containing selected HTTP response headers, or an error message.</returns>
        private async Task<string> GetHttpResponseHeaders(TcpClient tcpClient, string ip, int port, CancellationToken ct)
        {
            // NetworkStream stream = null;
            // SslStream sslStream = null;
            return "HTTP Check Disabled";
            try
            {
             var   stream = tcpClient.GetStream();
              var  sslStream = new SslStream(stream, false);

                if (port == 443 || port == 8443) // Handle HTTPS
                {
                    try
                    {

                        // The `ip` parameter is used here for the targetHostName,
                        // as we might not have a hostname for the IP address.
                        // Validation will fail if certificate's subject doesn't match the IP.
                        // You might need to add a RemoteCertificateValidationCallback
                        // to handle self-signed certs or IP mismatches if this is too strict.
                        await sslStream.AuthenticateAsClientAsync(ip, clientCertificates: null,
                                                                 enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13, // Prioritize modern TLS
                                                                 checkCertificateRevocation: false); // Can set to true for stricter checks
                      //  stream = sslStream; // Use the SSL stream for further communication
                    }
                    catch (AuthenticationException authEx)
                    {
                        return $"HTTPS Auth Error: {authEx.Message}";
                    }
                    catch (Exception sslEx)
                    {
                        return $"HTTPS SSL Error: {sslEx.Message}";
                    }
                }

                // Construct a minimal HTTP GET request for the root path
                // Using Host header with IP directly as we don't have domain names
                string httpRequest = $"GET / HTTP/1.1\r\nHost: {ip}\r\nConnection: close\r\n\r\n";
                byte[] requestBytes = Encoding.ASCII.GetBytes(httpRequest);

                await sslStream.WriteAsync(requestBytes, 0, requestBytes.Length, ct);

                // Read the response
                byte[] buffer = new byte[tcpClient.ReceiveBufferSize];
                StringBuilder responseBuilder = new StringBuilder();
                int bytesRead;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // Timeout for reading HTTP response
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                {
                    try
                    {
                        // Read until we encounter an empty line (end of headers) or timeout
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                        {
                            string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            responseBuilder.Append(chunk);

                            // Simple check for end of headers (double newline)
                            if (responseBuilder.ToString().Contains("\r\n\r\n"))
                            {
                                break;
                            }

                            // If we've read a lot of data and still no double newline, break to avoid OOM or infinite loop
                            if (responseBuilder.Length > 16 * 1024) // Cap response size to 16KB for headers
                            {
                                _progressLog.Enqueue($"    Too much data received for HTTP headers on {ip}:{port}, truncating.");
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return "HTTP/S header read timed out or cancelled.";
                    }
                }

                string fullResponse = responseBuilder.ToString();
                if (string.IsNullOrEmpty(fullResponse))
                {
                    return "No HTTP/S response received.";
                }

                // Extract relevant headers
                var lines = fullResponse.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var interestingHeaders = new List<string>();

                // Get the status line (e.g., HTTP/1.1 200 OK)
                if (lines.Length > 0)
                {
                    interestingHeaders.Add(lines[0]);
                }

                // Look for common headers
                foreach (var line in lines)
                {
                    if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("X-Powered-By:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)) // Useful for redirects
                    {
                        interestingHeaders.Add(line);
                    }
                    if (string.IsNullOrWhiteSpace(line)) // Stop after headers
                    {
                        break;
                    }
                }

                return string.Join(" | ", interestingHeaders).Trim();
            }
            catch (Exception ex)
            {
                return $"Error getting HTTP/S headers: {ex.Message}";
            }
            finally
            {
               // sslStream?.Dispose(); // Dispose SslStream if used
                // The underlying NetworkStream will be closed with TcpClient.Close() when tcpClient is disposed
            }
        }
        // ---
        // New method for updating NetworkMap.Hosts and triggering real-time updates
        // ---
        private void UpdateNetworkMapHost(Host newOrUpdatedHost)
        {
            lock (NetworkMap.Hosts) // Ensure thread-safe access to NetworkMap.Hosts
            {
                var ip = newOrUpdatedHost.NetworkInterfaces.First().Ip_Address.First();
                var existingHost = NetworkMap.Hosts
                    .FirstOrDefault(h => h.NetworkInterfaces.Any(ni => ni.Ip_Address.Contains(ip)));

                if (existingHost != null)
                {
                    var existingNetworkInterface = existingHost.NetworkInterfaces.FirstOrDefault(ni => ni.Ip_Address.Contains(ip));
                    if (existingNetworkInterface != null)
                    {
                        var newServices = newOrUpdatedHost.NetworkInterfaces.First().Services;
                        if (newServices != null && newServices.Any())
                        {
                            // Union with a ServiceComparer to avoid duplicate services
                            existingNetworkInterface.Services = existingNetworkInterface.Services
                                .Union(newServices, new ServiceComparer())
                                .ToList();
                        }
                    }
                    else
                    {
                        // Add the new network interface to the existing host
                        existingHost.NetworkInterfaces.Add(newOrUpdatedHost.NetworkInterfaces.First());
                    }
                    existingHost.Status = HostStatus.Online;
                }
                else
                {
                    // If host doesn't exist in NetworkMap, add it
                    newOrUpdatedHost.Status = HostStatus.Online;
                    NetworkMap.Hosts.Add(newOrUpdatedHost);
                }

                // **Notify subscribers that a host has been updated/found**
                HostFoundOrUpdated?.Invoke(existingHost ?? newOrUpdatedHost);
            }
        }

        // ---
        // ServiceComparer to use with Union to avoid duplicate services based on Port and Protocol
        // ---
        private class ServiceComparer : IEqualityComparer<Service>
        {
            public bool Equals(Service x, Service y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
                return x.Port == y.Port && x.Protocol == y.Protocol;
            }

            public int GetHashCode(Service obj)
            {
                return HashCode.Combine(obj.Port, obj.Protocol);
            }
        }

        // ---
        // Known Port Services Dictionary (moved out of class for static access)
        // ---
        public static Dictionary<int, string> KnownPortServices = new Dictionary<int, string>()
        {
            // Well-known Ports (0-1023)
            { 7, "echo" }, { 9, "discard" }, { 13, "daytime" }, { 17, "quote" }, { 19, "chargen" },
            { 20, "ftp-data" }, { 21, "ftp" }, { 22, "ssh" }, { 23, "telnet" }, { 25, "smtp" },
            { 53, "dns" }, { 67, "dhcp-server" }, { 68, "dhcp-client" }, { 69, "tftp" }, { 79, "finger" },
            { 80, "http" }, { 88, "kerberos" }, { 109, "pop2" }, { 110, "pop3" }, { 111, "rpcbind" },
            { 113, "ident" }, { 119, "nntp" }, { 123, "ntp" }, { 135, "msrpc" },
            { 137, "netbios-ns" }, { 138, "netbios-dgm" }, { 139, "netbios-ssn" },
            { 143, "imap" }, { 161, "snmp" }, { 162, "snmp-trap" }, { 177, "xdmcp" },
            { 389, "ldap" }, { 443, "https" }, { 445, "microsoft-ds" }, { 500, "isakmp" },
            { 514, "syslog" }, { 546, "dhcpv6-client" }, { 547, "dhcpv6-server" }, { 587, "submission" },
            { 636, "ldaps" }, { 993, "imaps" }, { 995, "pop3s" },

            // Registered Ports (1024-49151)
            { 1080, "socks" }, { 1433, "ms-sql-s" }, { 1434, "ms-sql-m" }, { 1521, "oracle" },
            { 1720, "h.323-q.931" }, { 1723, "pptp" }, { 3306, "mysql" }, { 3389, "rdp" },
            { 5060, "sip" }, { 5061, "sips" }, { 5432, "postgresql" }, { 5900, "vnc" },
            { 8080, "http-alt" }, { 8443, "https-alt" }, { 27017, "mongodb" },
            { 27018, "mongodb-shard" }, { 27019, "mongodb-config" },
        };
    }
}