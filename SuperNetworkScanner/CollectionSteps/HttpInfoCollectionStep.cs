using SuperNetworkScanner.Extensions;
using SuperNetworkScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkInterface = SuperNetworkScanner.Models.NetworkInterface;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace SuperNetworkScanner.CollectionSteps
{
    public class HttpInfoCollectionStep : ICollectionStep
    {
        public string Name => "HTTP/S Information Collector";
        public string Description => "Collects detailed information from HTTP/S services (Server header, OS/Software identification).";
        public string ProgressMessage { get; private set; }
        public string ProgressLog => string.Join("\r\n", _progressLog.Reverse());
        public decimal ProgressPercentage => TotalHostsToProcess == 0 ? 0 : (decimal)_completedHosts / TotalHostsToProcess;
        public bool IsCompleted { get; private set; }

        private int TotalHostsToProcess = 0;
        private int _completedHosts = 0;

        private readonly ConcurrentQueue<string> _progressLog = new ConcurrentQueue<string>();

        // Event to notify UI of host updates in real-time
        public event Action<Host> HostUpdated;

        /// <summary>
        /// Initiates the HTTP/S information collection process.
        /// It scans hosts already discovered with open HTTP/S ports (80 or 443)
        /// from the NetworkMap.Hosts collection.
        /// </summary>
        /// <param name="search_ips">This parameter is largely ignored as this step pulls data from NetworkMap.Hosts.</param>
        public void Start(List<string> search_ips)
        {
            IsCompleted = false;
            _completedHosts = 0;

            // Filter hosts that have open port 80 or 443
            var httpHttpsHosts = NetworkMap.Hosts
                .Where(h => h.NetworkInterfaces.Any(ni => ni.Services.Any(s => s.Port == 80 || s.Port == 443)))
                .ToList();

            TotalHostsToProcess = httpHttpsHosts.Count;

            if (!httpHttpsHosts.Any())
            {
                _progressLog.Enqueue("No hosts with open HTTP/S ports found for detailed collection.");
                IsCompleted = true;
                return;
            }

            ProgressMessage = "Starting HTTP/S information collection...";
            _progressLog.Enqueue($"Found {TotalHostsToProcess} hosts with HTTP/S services to analyze.");

            // Offload the scanning process to a background task to keep the UI responsive
            Task.Run(async () =>
            {
                await Parallel.ForEachAsync(httpHttpsHosts, new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (host, ct) =>
                {
                    await ProcessHostHttpInfo(host, ct);
                    Interlocked.Increment(ref _completedHosts);
                    ProgressMessage = $"Processed {_completedHosts} of {TotalHostsToProcess} HTTP/S hosts.";
                });

                IsCompleted = true;
                ProgressMessage = "HTTP/S information collection completed.";
                _progressLog.Enqueue("HTTP/S information collection finished.");
            });
        }

        /// <summary>
        /// Processes a single host to gather more detailed HTTP/S information.
        /// Re-attempts banner grabbing if previous attempt resulted in an error or was empty,
        /// then parses the "Server" header to populate Service.ServiceName, Host.Manufacturer, and Host.Model.
        /// </summary>
        /// <param name="host">The Host object to process.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        private async Task ProcessHostHttpInfo(Host host, CancellationToken ct)
        {
            var ip = host.NetworkInterfaces.First().Ip_Address.First();
            _progressLog.Enqueue($"Processing HTTP/S info for {ip}...");

            var networkInterface = host.NetworkInterfaces.First(); // Assuming one interface for simplicity

            foreach (var service in networkInterface.Services.Where(s => s.Port == 80 || s.Port == 443))
            {
                if (ct.IsCancellationRequested)
                {
                    _progressLog.Enqueue($"Processing for {ip} cancelled.");
                    break;
                }

                // Only attempt to re-grab if the service description suggests an error or it's empty
                // Otherwise, proceed to parse existing description.
                if (service.Description?.StartsWith("Error", StringComparison.OrdinalIgnoreCase) == true || string.IsNullOrWhiteSpace(service.Description))
                {
                    _progressLog.Enqueue($"  Re-attempting HTTP/S header grab for {ip}:{service.Port}");
                    using var tcpClient = new TcpClient();
                    tcpClient.ReceiveBufferSize = 8192; // Increased buffer for HTTP responses
                    tcpClient.SendTimeout = 3000;       // Timeout for sending data
                    tcpClient.ReceiveTimeout = 3000;    // Timeout for receiving data

                    try
                    {
                        var connectTask = tcpClient.ConnectAsync(ip, service.Port);
                        var timeoutTask = Task.Delay(3000, ct);

                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                        if (completedTask == connectTask && tcpClient.Connected)
                        {
                            string newDescription = await GetHttpResponseHeaders(tcpClient, ip, service.Port, ct);

                            // Update service description if a valid response was received
                            if (!string.IsNullOrWhiteSpace(newDescription) && !newDescription.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                            {
                                service.Description = newDescription;
                                _progressLog.Enqueue($"    Updated description for {ip}:{service.Port}: {newDescription}");

                                // Attempt to parse Server header for ServiceName, Manufacturer/Model
                                ParseServerHeader(host, service, newDescription);
                            }
                            else if (!string.IsNullOrWhiteSpace(newDescription))
                            {
                                // Still an error, but update the description with the new error message
                                service.Description = newDescription;
                                _progressLog.Enqueue($"    Still error for {ip}:{service.Port}: {newDescription}");
                            }
                            else
                            {
                                service.Description = "No HTTP/S response or empty response.";
                                _progressLog.Enqueue($"    No HTTP/S response for {ip}:{service.Port}.");
                            }
                        }
                        else
                        {
                            service.Description = "Connection/Timeout error during re-attempt.";
                            _progressLog.Enqueue($"    Connection/Timeout error for {ip}:{service.Port} during re-attempt.");
                        }
                    }
                    catch (Exception ex)
                    {
                        service.Description = $"Error re-grabbing HTTP/S info: {ex.Message}";
                        _progressLog.Enqueue($"    Error re-grabbing HTTP/S info for {ip}:{service.Port}: {ex.Message}");
                    }
                    finally
                    {
                        tcpClient.Close(); // Ensure the TCP client is closed
                    }
                }
                else
                {
                    _progressLog.Enqueue($"  HTTP/S service on {ip}:{service.Port} already has valid info. Parsing existing.");
                    // Still attempt to parse the existing description for ServiceName, Manufacturer/Model
                    ParseServerHeader(host, service, service.Description);
                }

                // Notify UI that this host has been updated
                HostUpdated?.Invoke(host);
            }
        }

        /// <summary>
        /// Parses the HTTP response headers for a "Server" header and attempts to extract
        /// web server software, OS, Manufacturer, and Model information.
        /// Updates Service.ServiceName, Host.Manufacturer, and Host.Model.
        /// </summary>
        /// <param name="host">The host object to update.</param>
        /// <param name="service">The service object (port 80 or 443) to update its ServiceName.</param>
        /// <param name="httpResponseHeaders">The string containing HTTP response headers.</param>
        private void ParseServerHeader(Host host, Service service, string httpResponseHeaders)
        {
            if (string.IsNullOrWhiteSpace(httpResponseHeaders)) return;

            var serverMatch = Regex.Match(httpResponseHeaders, @"Server:\s*([^\|]+)", RegexOptions.IgnoreCase);
            if (serverMatch.Success)
            {
                var serverString = serverMatch.Groups[1].Value.Trim();
                _progressLog.Enqueue($"    Found Server header: '{serverString}' for host {host.NetworkInterfaces.First().Ip_Address.First()}");

                string detectedSoftware = null;
                string detectedSoftwareVersion = null;
                string detectedOS = null;
                string detectedOSVersion = null;

                // --- 1. Detect Web Server Software and Version ---
                if (serverString.Contains("nginx", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Nginx";
                    detectedSoftwareVersion = ExtractVersion(serverString, "nginx");
                }
                else if (serverString.Contains("Apache", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Apache HTTP Server";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Apache");
                }
                else if (serverString.Contains("Microsoft-IIS", StringComparison.OrdinalIgnoreCase) || serverString.Contains("IIS", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Microsoft IIS";
                    detectedSoftwareVersion = ExtractVersion(serverString, "IIS") ?? ExtractVersion(serverString, "Microsoft-IIS");
                }
                else if (serverString.Contains("lighttpd", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Lighttpd";
                    detectedSoftwareVersion = ExtractVersion(serverString, "lighttpd");
                }
                else if (serverString.Contains("openresty", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "OpenResty";
                    detectedSoftwareVersion = ExtractVersion(serverString, "openresty");
                }
                else if (serverString.Contains("Node.js", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Node.js";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Node.js");
                }
                else if (serverString.Contains("Express", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Express.js";
                    // Version might be in the form "Express/4.17.1", or not present
                    detectedSoftwareVersion = ExtractVersion(serverString, "Express");
                }
                else if (serverString.Contains("Tomcat", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Apache Tomcat";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Tomcat");
                }
                else if (serverString.Contains("Jetty", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Eclipse Jetty";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Jetty");
                }
                else if (serverString.Contains("Caddy", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Caddy Server";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Caddy");
                }
                else if (serverString.Contains("Gunicorn", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Gunicorn";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Gunicorn");
                }
                else if (serverString.Contains("Kestrel", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "Kestrel (.NET)";
                    detectedSoftwareVersion = ExtractVersion(serverString, "Kestrel");
                }
                else if (serverString.Contains("php", StringComparison.OrdinalIgnoreCase) || serverString.Contains("HHVM", StringComparison.OrdinalIgnoreCase))
                {
                    detectedSoftware = "PHP"; // Indicates PHP is running, often with Apache/Nginx
                    detectedSoftwareVersion = ExtractVersion(serverString, "php") ?? ExtractVersion(serverString, "HHVM");
                }
                // Fallback for software if no specific match, try first word
                if (string.IsNullOrEmpty(detectedSoftware))
                {
                    var firstWord = serverString.Split(' ', '/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstWord))
                    {
                        detectedSoftware = firstWord.TrimEnd('/', ':');
                        // Attempt to extract version following the first word if it looks like one
                        detectedSoftwareVersion = ExtractVersion(serverString, firstWord);
                    }
                }

                // Update ServiceName based on detected software
                if (!string.IsNullOrEmpty(detectedSoftware))
                {
                    var baseServiceName = (service.Port == 443) ? "https" : "http";
                    string newServiceName = baseServiceName + " " + detectedSoftware;
                    if (!string.IsNullOrEmpty(detectedSoftwareVersion))
                    {
                        newServiceName += " " + detectedSoftwareVersion;
                    }

                    // Only update if the current name is generic or if the new one is more specific
                    if (service.ServiceName == baseServiceName || service.ServiceName == "Port " + service.Port ||
                        !service.ServiceName.Contains(detectedSoftware, StringComparison.OrdinalIgnoreCase))
                    {
                        service.ServiceName = newServiceName;
                        _progressLog.Enqueue($"      Service Name updated to: {service.ServiceName}");
                    }
                }


                // --- 2. Detect Operating System / Vendor ---
                // Look for OS identifiers, typically in parentheses or trailing info
                if (serverString.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Ubuntu Linux";
                    detectedOSVersion = ExtractVersion(serverString, "Ubuntu");
                }
                else if (serverString.Contains("Debian", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Debian Linux";
                    detectedOSVersion = ExtractVersion(serverString, "Debian");
                }
                else if (serverString.Contains("CentOS", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "CentOS Linux";
                    detectedOSVersion = ExtractVersion(serverString, "CentOS");
                }
                else if (serverString.Contains("Red Hat", StringComparison.OrdinalIgnoreCase) || serverString.Contains("RHEL", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Red Hat Enterprise Linux";
                    detectedOSVersion = ExtractVersion(serverString, "Red Hat") ?? ExtractVersion(serverString, "RHEL");
                }
                else if (serverString.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Microsoft Windows Server";
                    detectedOSVersion = ExtractVersion(serverString, "Windows"); // e.g., Windows/NT
                    // Further parsing might be needed for specific Windows versions if present
                }
                else if (serverString.Contains("FreeBSD", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "FreeBSD";
                    detectedOSVersion = ExtractVersion(serverString, "FreeBSD");
                }
                else if (serverString.Contains("macOS", StringComparison.OrdinalIgnoreCase) || serverString.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Apple macOS Server";
                    detectedOSVersion = ExtractVersion(serverString, "macOS") ?? ExtractVersion(serverString, "Darwin");
                }
                else if (serverString.Contains("Linux", StringComparison.OrdinalIgnoreCase))
                {
                    detectedOS = "Linux"; // Generic Linux if no specific distro found
                    detectedOSVersion = ExtractVersion(serverString, "Linux");
                }

                // --- 3. Populate Host.Manufacturer and Host.Model ---
                // Manufacturer (hardware vendor) is very difficult to get from Server header alone.
                // We'll primarily set it for known OS vendors.
                string hostManufacturer = null;
                string hostModel = null;

                if (!string.IsNullOrEmpty(detectedOS))
                {
                    if (detectedOS.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        hostManufacturer = "Microsoft";
                        hostModel = detectedOS + (string.IsNullOrEmpty(detectedOSVersion) ? "" : " " + detectedOSVersion);
                    }
                    else if (detectedOS.StartsWith("Apple", StringComparison.OrdinalIgnoreCase))
                    {
                        hostManufacturer = "Apple Inc.";
                        hostModel = detectedOS + (string.IsNullOrEmpty(detectedOSVersion) ? "" : " " + detectedOSVersion);
                    }
                    else if (detectedOS.Contains("Linux", StringComparison.OrdinalIgnoreCase))
                    {
                        // For Linux, the manufacturer is typically the hardware vendor, which Server header doesn't provide.
                        // We can set the Model to the Linux distribution/version.
                        hostModel = detectedOS + (string.IsNullOrEmpty(detectedOSVersion) ? "" : " " + detectedOSVersion);
                    }
                    else
                    {
                        hostModel = detectedOS + (string.IsNullOrEmpty(detectedOSVersion) ? "" : " " + detectedOSVersion);
                    }
                }
                // If Host.Model is still empty, and we detected software, put that there.
                if (string.IsNullOrEmpty(hostModel) && !string.IsNullOrEmpty(detectedSoftware))
                {
                    hostModel = detectedSoftware + (string.IsNullOrEmpty(detectedSoftwareVersion) ? "" : " " + detectedSoftwareVersion);
                }


                // Set host Manufacturer and Model only if they are not already set or are null/empty.
                // This prevents overwriting information from other steps if it's already more accurate.
                if (string.IsNullOrEmpty(host.Manufacturer) && !string.IsNullOrEmpty(hostManufacturer))
                {
                    host.Manufacturer = hostManufacturer;
                    _progressLog.Enqueue($"      Host Manufacturer set to: {hostManufacturer}");
                }
                if (string.IsNullOrEmpty(host.Model) && !string.IsNullOrEmpty(hostModel))
                {
                    host.Model = hostModel;
                    _progressLog.Enqueue($"      Host Model set to: {hostModel}");
                }
            }
        }

        /// <summary>
        /// Helper method to extract version numbers from strings using regex.
        /// Looks for a pattern like "keyword/version" or "keyword version".
        /// </summary>
        /// <param name="sourceString">The full string to search within (e.g., "Apache/2.4.41 (Ubuntu)").</param>
        /// <param name="keyword">The keyword preceding the version (e.g., "Apache", "nginx", "Windows").</param>
        /// <returns>The extracted version string, or null if not found.</returns>
        private string ExtractVersion(string sourceString, string keyword)
        {
            // This regex tries to capture version numbers immediately following the keyword
            // separated by a slash, space, or just directly.
            // It looks for patterns like:
            // "Keyword/1.2.3" -> captures "1.2.3"
            // "Keyword 1.2.3" -> captures "1.2.3"
            // "Keyword1.2.3" (less common but possible for some) -> captures "1.2.3"
            var match = Regex.Match(sourceString, $@"{Regex.Escape(keyword)}[/\s]?(\d+(\.\d+)*(\.\d+)*)?", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                return match.Groups[1].Value;
            }
            return null;
        }

        /// <summary>
        /// Attempts to send an HTTP GET request and read response headers from a connected TCP client.
        /// Handles both HTTP and HTTPS connections.
        /// This is a utility method, potentially reusable by other steps.
        /// </summary>
        /// <param name="tcpClient">The connected TcpClient.</param>
        /// <param name="ip">The IP address of the target.</param>
        /// <param name="port">The port of the target (e.g., 80 for HTTP, 443 for HTTPS).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A string containing selected HTTP response headers, or an error message.</returns>
        private async Task<string> GetHttpResponseHeaders(TcpClient tcpClient, string ip, int port, CancellationToken ct)
        {
            System.IO.Stream activeStream = null; // Use a base Stream type for NetworkStream or SslStream

            try
            {
                NetworkStream networkStream = tcpClient.GetStream();

                if (port == 443 || port == 8443) // Handle HTTPS connections
                {
                    SslStream sslStream = new SslStream(networkStream, false, userCertificateValidationCallback: (sender, certificate, chain, sslPolicyErrors) => true);
                    try
                    {
                        await sslStream.AuthenticateAsClientAsync(
    ip,
    clientCertificates: null,
    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
    checkCertificateRevocation: false);
                        activeStream = sslStream; // Use the SslStream for further communication
                    }
                    catch (AuthenticationException authEx)
                    {
                        return $"Error: HTTPS Auth Error: {authEx.Message}";
                    }
                    catch (Exception sslEx)
                    {
                        return $"Error: HTTPS SSL Error: {sslEx.Message}";
                    }
                }
                else
                {
                    activeStream = networkStream; // For HTTP, use the raw NetworkStream
                }

                // Construct a minimal HTTP GET request for the root path
                // Using Host header with IP directly as we don't have domain names
                string httpRequest = $"GET / HTTP/1.1\r\nHost: {ip}\r\nConnection: close\r\n\r\n";
                byte[] requestBytes = Encoding.ASCII.GetBytes(httpRequest);

                await activeStream.WriteAsync(requestBytes, 0, requestBytes.Length, ct); // Send the HTTP request

                byte[] buffer = new byte[tcpClient.ReceiveBufferSize];
                StringBuilder responseBuilder = new StringBuilder();
                int bytesRead;

                // Use a CancellationTokenSource for a specific read timeout, linked with the main cancellation token
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                {
                    try
                    {
                        // Read from the stream until end of headers or timeout
                        while ((bytesRead = await activeStream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                        {
                            string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            responseBuilder.Append(chunk);

                            // Check for the end of HTTP headers (empty line)
                            if (responseBuilder.ToString().Contains("\r\n\r\n"))
                            {
                                break;
                            }
                            // Cap the response size to avoid reading huge bodies
                            if (responseBuilder.Length > 16 * 1024)
                            {
                                return "Error: Too much data received for HTTP headers.";
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return "Error: HTTP/S header read timed out or cancelled.";
                    }
                }

                string fullResponse = responseBuilder.ToString();
                if (string.IsNullOrEmpty(fullResponse))
                {
                    return "Error: No HTTP/S response received.";
                }

                // Extract relevant headers for storing and parsing
                var lines = fullResponse.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var interestingHeaders = new List<string>();

                // Always add the status line (e.g., "HTTP/1.1 200 OK")
                if (lines.Length > 0)
                {
                    interestingHeaders.Add(lines[0]);
                }

                // Look for common informational headers
                foreach (var line in lines)
                {
                    if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("X-Powered-By:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)) // Useful for redirects
                    {
                        interestingHeaders.Add(line);
                    }
                    if (string.IsNullOrWhiteSpace(line)) // Stop after the header section
                    {
                        break;
                    }
                }

                return string.Join(" | ", interestingHeaders).Trim();
            }
            catch (Exception ex)
            {
                return $"Error: Getting HTTP/S headers failed: {ex.Message}";
            }
            finally
            {
                // Ensure the active stream (either NetworkStream or SslStream) is disposed.
                // Disposing SslStream also disposes its underlying NetworkStream.
                activeStream?.Dispose();
            }
        }
    }
}