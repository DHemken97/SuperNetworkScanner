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
using System.Net; // For IPAddress
using NetworkInterface = SuperNetworkScanner.Models.NetworkInterface; // Alias for clarity

namespace SuperNetworkScanner.CollectionSteps
{
    public class MsrpcInfoCollectionStep : ICollectionStep
    {
        public string Name => "MSRPC Information Collector";
        public string Description => "Connects to MSRPC Endpoint Mapper (Port 135) to identify running RPC services and potential OS info.";
        public string ProgressMessage { get; private set; }
        public string ProgressLog => string.Join("\r\n", _progressLog.Reverse());
        public decimal ProgressPercentage => TotalHostsToProcess == 0 ? 0 : (decimal)_completedHosts / TotalHostsToProcess;
        public bool IsCompleted { get; private set; }

        private int TotalHostsToProcess = 0;
        private int _completedHosts = 0;

        private readonly ConcurrentQueue<string> _progressLog = new ConcurrentQueue<string>();

        public event Action<Host> HostUpdated;

        // Default MSRPC ports to check if not explicitly found by PortScanStep
        private readonly List<int> MsrpcPorts = new List<int> { 135, 445 }; // 135 for Endpoint Mapper, 445 for SMB (often uses RPC)

        public void Start(List<string> search_ips)
        {
            IsCompleted = false;
            _completedHosts = 0;

            // Filter hosts that have open MSRPC-related ports (135 or 445)
            // We assume PortScanStep has already populated NetworkMap.Hosts
            var msrpcHosts = NetworkMap.Hosts
                .Where(h => h.NetworkInterfaces.Any(ni => ni.Services.Any(s => MsrpcPorts.Contains(s.Port))))
                .ToList();

            TotalHostsToProcess = msrpcHosts.Count;

            if (!msrpcHosts.Any())
            {
                _progressLog.Enqueue("No hosts with open MSRPC ports (135, 445) found for detailed collection.");
                IsCompleted = true;
                return;
            }

            ProgressMessage = "Starting MSRPC information collection...";
            _progressLog.Enqueue($"Found {TotalHostsToProcess} hosts with MSRPC services to analyze.");

            Task.Run(async () =>
            {
                await Parallel.ForEachAsync(msrpcHosts, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (host, ct) =>
                {
                    await ProcessHostMsrpcInfo(host, ct);
                    Interlocked.Increment(ref _completedHosts);
                    ProgressMessage = $"Processed {_completedHosts} of {TotalHostsToProcess} MSRPC hosts.";
                });

                IsCompleted = true;
                ProgressMessage = "MSRPC information collection completed.";
                _progressLog.Enqueue("MSRPC information collection finished.");
            });
        }

        private async Task ProcessHostMsrpcInfo(Host host, CancellationToken ct)
        {
            var ip = host.NetworkInterfaces.FirstOrDefault()?.Ip_Address.FirstOrDefault();
            if (string.IsNullOrEmpty(ip)) return;

            _progressLog.Enqueue($"Processing MSRPC info for {ip}...");

            var networkInterface = host.NetworkInterfaces.FirstOrDefault();
            if (networkInterface == null) return;

            // Prioritize port 135 as it's the Endpoint Mapper
            var msrpcServices = networkInterface.Services
                .Where(s => MsrpcPorts.Contains(s.Port))
                .OrderBy(s => s.Port == 135 ? 0 : 1) // Ensure 135 is processed first
                .ToList();

            foreach (var service in msrpcServices)
            {
                if (ct.IsCancellationRequested)
                {
                    _progressLog.Enqueue($"Processing for {ip} cancelled.");
                    break;
                }

                // Only re-probe if the service description suggests an error or is empty
                if (service.Description?.StartsWith("Error", StringComparison.OrdinalIgnoreCase) == true || string.IsNullOrWhiteSpace(service.Description))
                {
                    _progressLog.Enqueue($"  Re-attempting MSRPC probe for {ip}:{service.Port}");
                    string newDescription = await ProbeMsrpc(ip, service.Port, ct);

                    if (!string.IsNullOrWhiteSpace(newDescription) && !newDescription.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        service.Description = newDescription;
                        _progressLog.Enqueue($"    Updated description for {ip}:{service.Port}: {newDescription}");
                        ParseMsrpcInfo(host, service, newDescription); // Try to parse OS/Service info
                    }
                    else
                    {
                        // Update with new error or indication if nothing useful came back
                        service.Description = newDescription ?? "No MSRPC response or empty response.";
                        _progressLog.Enqueue($"    Still error/no response for {ip}:{service.Port}: {service.Description}");
                    }
                }
                else
                {
                    _progressLog.Enqueue($"  MSRPC service on {ip}:{service.Port} already has valid info. Parsing existing.");
                    ParseMsrpcInfo(host, service, service.Description); // Parse existing info
                }

                HostUpdated?.Invoke(host);
            }
        }

        /// <summary>
        /// Attempts to perform a basic MSRPC probe on the specified port.
        /// This is a simplified approach, focusing on Endpoint Mapper (port 135) for now.
        /// For full MSRPC functionality, a dedicated library would be needed.
        /// </summary>
        /// <param name="ip">The target IP address.</param>
        /// <param name="port">The target port (e.g., 135).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A string containing details of the MSRPC response or an error message.</returns>
        private async Task<string> ProbeMsrpc(string ip, int port, CancellationToken ct)
        {
            using var tcpClient = new TcpClient();
            tcpClient.ReceiveBufferSize = 4096;
            tcpClient.SendTimeout = 3000;
            tcpClient.ReceiveTimeout = 3000;

            try
            {
                var connectTask = tcpClient.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000, ct);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && tcpClient.Connected)
                {
                    using var stream = tcpClient.GetStream();

                    // MSRPC Endpoint Mapper (Port 135) Probe:
                    // This is a minimal DCE/RPC bind request packet for the Endpoint Mapper interface.
                    // It requests a specific interface (UUID for Endpoint Mapper) and a specific version.
                    // This is a simplified example; full MSRPC requires more complex packet crafting.
                    // Packet structure:
                    // Version (1 byte)
                    // VersionMinor (1 byte)
                    // PacketType (1 byte) - e.g., 0x05 for bind
                    // PacketFlags (1 byte)
                    // DataRepresentation (4 bytes) - Endianness, char/float format
                    // FragLength (2 bytes) - Total fragment length
                    // AuthLength (2 bytes)
                    // CallId (4 bytes)
                    // ContextCount (2 bytes)
                    // MaxXmitFrag (2 bytes)
                    // MaxRecvFrag (2 bytes)
                    // AssocGroup (4 bytes)
                    // Context (variable) - PContext (Presentation Context)
                    //   - ContextId (2 bytes)
                    //   - NumTransferSyn (2 bytes)
                    //   - TransferSyntax (16 bytes UUID, 4 bytes version)
                    // Interface (16 bytes UUID, 4 bytes version)

                    if (port == 135)
                    {
                        // This is a minimal RPC Bind Request for the Endpoint Mapper (UUID: E1AF8200-7F52-11CE-90F4-00AA006BF1A8)
                        // This packet aims to get a bind ACK/NAK or some initial response.
                        byte[] rpcBindRequest = new byte[]
                        {
                            0x05, 0x00, 0x0B, 0x03, 0x10, 0x00, 0x00, 0x00, // Version, Flags, DataRep
                            0x58, 0x00, 0x00, 0x00, // FragLength, AuthLength
                            0x01, 0x00, 0x00, 0x00, // CallId
                            0x01, 0x00, // ContextCount
                            0x00, 0x10, // MaxXmitFrag (4096)
                            0x00, 0x10, // MaxRecvFrag (4096)
                            0x00, 0x00, 0x00, 0x00, // AssocGroup
                            // Presentation Context (1 of 1)
                            0x00, 0x00, // ContextId
                            0x01, 0x00, // NumTransferSyntaxes (1)
                            // Abstract Syntax (Endpoint Mapper Interface)
                            0x00, 0x82, 0xAF, 0xE1, 0x52, 0x7F, 0xCE, 0x11, 0xF4, 0x90, 0x00, 0xAA, 0x00, 0x6B, 0xF1, 0xA8, // UUID
                            0x03, 0x00, 0x00, 0x00, // Version (3.0)
                            // Transfer Syntax (NDR)
                            0x04, 0x5D, 0x88, 0x8A, 0xEB, 0x1C, 0xC9, 0x11, 0x9F, 0xE8, 0x08, 0x00, 0x2B, 0x10, 0x48, 0x60, // UUID
                            0x02, 0x00, 0x00, 0x00 // Version (2.0)
                        };

                        await stream.WriteAsync(rpcBindRequest, 0, rpcBindRequest.Length, ct);

                        byte[] buffer = new byte[tcpClient.ReceiveBufferSize];
                        StringBuilder responseBuilder = new StringBuilder();
                        int bytesRead;

                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                        {
                            try
                            {
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                                {
                                    responseBuilder.Append(BitConverter.ToString(buffer, 0, bytesRead)); // Hex string representation
                                    if (responseBuilder.Length > 4096) break; // Limit response size
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                return "Error: MSRPC probe timed out or cancelled.";
                            }
                        }

                        string hexResponse = responseBuilder.ToString();
                        if (string.IsNullOrEmpty(hexResponse))
                        {
                            return "Error: No MSRPC response received.";
                        }

                        // Basic check for RPC Bind ACK (Packet Type 0x0C)
                        // A more robust parser would be needed for full understanding
                        if (hexResponse.Length >= 6 && hexResponse.Substring(4, 2) == "0C") // 5th byte is PacketType for Bind ACK
                        {
                            return $"MSRPC Endpoint Mapper (Port 135) - Bind ACK. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}..."; // Truncate for display
                        }
                        else
                        {
                            return $"MSRPC Endpoint Mapper (Port 135) - Non-ACK response or malformed. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}...";
                        }
                    }
                    else if (port == 445) // Basic SMB/RPC detection
                    {
                        // SMB (Server Message Block) often sits on 445 and uses RPC.
                        // A direct probe for SMB itself is complicated. We can send a null
                        // SMB Negotiate Protocol request to see if it responds.
                        byte[] smbNegotiateRequest = new byte[]
                        {
                            0x00, 0x00, 0x00, 0x85, // NetBIOS session service header (length of SMB message)
                            0xFF, 0x53, 0x4D, 0x42, // SMB Header: Server Component (FF, SMB)
                            0x72, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Command (0x72=Negotiate), Reserved
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Status
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Flags, Flags2, PID, SID
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Tree ID, Proc ID, UID, MID
                            0x18, 0x00, // WordCount (24)
                            0x00, 0x00, // AndXCommand, Reserved
                            0x01, 0x00, // ByteCount (1)
                            0x02, // Dialect count (NT LM 0.12)
                            0x02, 0x4E, 0x54, 0x20, 0x4C, 0x4D, 0x20, 0x30, 0x2E, 0x31, 0x32, 0x00 // "NT LM 0.12"
                        };

                        await stream.WriteAsync(smbNegotiateRequest, 0, smbNegotiateRequest.Length, ct);

                        byte[] buffer = new byte[tcpClient.ReceiveBufferSize];
                        StringBuilder responseBuilder = new StringBuilder();
                        int bytesRead;

                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                        {
                            try
                            {
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token)) > 0)
                                {
                                    responseBuilder.Append(BitConverter.ToString(buffer, 0, bytesRead));
                                    if (responseBuilder.Length > 4096) break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                return "Error: SMB/MSRPC probe timed out or cancelled.";
                            }
                        }

                        string hexResponse = responseBuilder.ToString();
                        if (string.IsNullOrEmpty(hexResponse))
                        {
                            return "Error: No SMB/MSRPC response received on 445.";
                        }

                        // Look for SMB header (FF-53-4D-42) in the response
                        if (hexResponse.Contains("FF-53-4D-42"))
                        {
                            return $"SMB/MSRPC (Port 445) detected. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}...";
                        }
                        else
                        {
                            return $"Port 445 responded, but no SMB/MSRPC signature. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}...";
                        }
                    }
                    else
                    {
                        return $"MSRPC probe not implemented for port {port}.";
                    }
                }
                else
                {
                    return "Connection/Timeout error during MSRPC probe.";
                }
            }
            catch (Exception ex)
            {
                return $"Error during MSRPC probe: {ex.Message}";
            }
        }

        /// <summary>
        /// Parses MSRPC probe responses to update Host and Service properties.
        /// This is highly dependent on the quality and format of the 'response' string.
        /// </summary>
        /// <param name="host">The host object to update.</param>
        /// <param name="service">The service object to update.</param>
        /// <param name="response">The raw MSRPC probe response string.</param>
        private void ParseMsrpcInfo(Host host, Service service, string response)
        {
            // Update ServiceName
            string currentServiceName = service.ServiceName.ToLower();
            string newServiceSuffix = null;

            if (service.Port == 135)
            {
                if (response.Contains("Bind ACK", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "RPC Endpoint Mapper (Active)";
                }
                else if (response.Contains("Non-ACK", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "RPC Endpoint Mapper (Non-ACK)";
                }
                else
                {
                    newServiceSuffix = "RPC Endpoint Mapper (Unknown Response)";
                }
            }
            else if (service.Port == 445)
            {
                if (response.Contains("SMB/MSRPC detected", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "SMB/RPC (Active)";
                }
                else if (response.Contains("no SMB/MSRPC signature", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "SMB/RPC (No Signature)";
                }
                else
                {
                    newServiceSuffix = "SMB/RPC (Unknown Response)";
                }
            }

            if (!string.IsNullOrEmpty(newServiceSuffix))
            {
                if (currentServiceName == "port " + service.Port || currentServiceName == "msrpc" || !currentServiceName.Contains(newServiceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    service.ServiceName = $"MSRPC {newServiceSuffix}";
                    _progressLog.Enqueue($"      Service Name updated for {host.NetworkInterfaces.First().Ip_Address.First()}:{service.Port} to: {service.ServiceName}");
                }
            }


            // Attempt to deduce Manufacturer/Model (OS) from MSRPC responses
            // This is challenging without deeper protocol parsing, but some common patterns might exist.
            // For example, if you get a very specific error code or feature set indicative of a Windows version.
            // Placeholder: A real implementation would require parsing specific MSRPC packets.

            // Example of very basic OS deduction (highly unreliable without more data)
            if (response.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(host.Manufacturer))
                {
                    host.Manufacturer = "Microsoft";
                    _progressLog.Enqueue($"      Host Manufacturer set to: Microsoft (from MSRPC)");
                }
                if (string.IsNullOrEmpty(host.Model))
                {
                    host.Model = "Windows Server"; // Generic
                    _progressLog.Enqueue($"      Host Model set to: Windows Server (from MSRPC)");
                }
            }
            // More specific OS detection would require parsing specific RPC interface UUIDs or SMB dialects.
        }
    }
}