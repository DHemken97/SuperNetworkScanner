using SuperNetworkScanner.Extensions;
using SuperNetworkScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net; // For IPAddress
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkInterface = SuperNetworkScanner.Models.NetworkInterface; // Alias for clarity

namespace SuperNetworkScanner.CollectionSteps
{
    public class NetBiosInfoCollectionStep : ICollectionStep
    {
        public string Name => "NetBIOS Information Collector";
        public string Description => "Collects NetBIOS name table information (computer name, workgroup/domain, users) from UDP/137 and TCP/139.";
        public string ProgressMessage { get; private set; }
        public string ProgressLog => string.Join("\r\n", _progressLog.Reverse());
        public decimal ProgressPercentage => TotalHostsToProcess == 0 ? 0 : (decimal)_completedHosts / TotalHostsToProcess;
        public bool IsCompleted { get; private set; }

        private int TotalHostsToProcess = 0;
        private int _completedHosts = 0;

        private readonly ConcurrentQueue<string> _progressLog = new ConcurrentQueue<string>();

        public event Action<Host> HostUpdated;

        // NetBIOS ports to check
        private readonly List<int> NetBiosPorts = new List<int> { 137, 139 }; // UDP 137 for Name Service, TCP 139 for Session Service (SMB over NetBIOS)

        public void Start(List<string> search_ips)
        {
            IsCompleted = false;
            _completedHosts = 0;

            // Filter hosts that have open NetBIOS-related ports (137 or 139)
            var netbiosHosts = NetworkMap.Hosts
                .Where(h => h.NetworkInterfaces.Any(ni => ni.Services.Any(s => NetBiosPorts.Contains(s.Port))))
                .ToList();

            TotalHostsToProcess = netbiosHosts.Count;

            if (!netbiosHosts.Any())
            {
                _progressLog.Enqueue("No hosts with open NetBIOS ports (137, 139) found for detailed collection.");
                IsCompleted = true;
                return;
            }

            ProgressMessage = "Starting NetBIOS information collection...";
            _progressLog.Enqueue($"Found {TotalHostsToProcess} hosts with NetBIOS services to analyze.");

            Task.Run(async () =>
            {
                await Parallel.ForEachAsync(netbiosHosts, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (host, ct) =>
                {
                    await ProcessHostNetBiosInfo(host, ct);
                    Interlocked.Increment(ref _completedHosts);
                    ProgressMessage = $"Processed {_completedHosts} of {TotalHostsToProcess} NetBIOS hosts.";
                });

                IsCompleted = true;
                ProgressMessage = "NetBIOS information collection completed.";
                _progressLog.Enqueue("NetBIOS information finished.");
            });
        }

        private async Task ProcessHostNetBiosInfo(Host host, CancellationToken ct)
        {
            var ip = host.NetworkInterfaces.FirstOrDefault()?.Ip_Address.FirstOrDefault();
            if (string.IsNullOrEmpty(ip)) return;

            _progressLog.Enqueue($"Processing NetBIOS info for {ip}...");

            var networkInterface = host.NetworkInterfaces.FirstOrDefault();
            if (networkInterface == null) return;

            // Prioritize UDP 137 for Name Service query
            var netbiosServices = networkInterface.Services
                .Where(s => NetBiosPorts.Contains(s.Port))
                .OrderBy(s => s.Port == 137 ? 0 : 1) // UDP 137 first
                .ToList();

            foreach (var service in netbiosServices)
            {
                if (ct.IsCancellationRequested)
                {
                    _progressLog.Enqueue($"Processing for {ip} cancelled.");
                    break;
                }

                // Only re-probe if the service description suggests an error or is empty
                if (service.Description?.StartsWith("Error", StringComparison.OrdinalIgnoreCase) == true || string.IsNullOrWhiteSpace(service.Description))
                {
                    _progressLog.Enqueue($"  Re-attempting NetBIOS probe for {ip}:{service.Port}");
                    string newDescription = "";

                    if (service.Port == 137)
                    {
                        newDescription = await ProbeNetBiosUdp137(ip, ct);
                    }
                    else if (service.Port == 139)
                    {
                        newDescription = await ProbeNetBiosTcp139(ip, ct);
                    }

                    if (!string.IsNullOrWhiteSpace(newDescription) && !newDescription.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        service.Description = newDescription;
                        _progressLog.Enqueue($"    Updated description for {ip}:{service.Port}: {newDescription}");
                        ParseNetBiosInfo(host, service, newDescription); // Try to parse OS/Service info
                    }
                    else
                    {
                        service.Description = newDescription ?? "No NetBIOS response or empty response.";
                        _progressLog.Enqueue($"    Still error/no response for {ip}:{service.Port}: {service.Description}");
                    }
                }
                else
                {
                    _progressLog.Enqueue($"  NetBIOS service on {ip}:{service.Port} already has valid info. Parsing existing.");
                    ParseNetBiosInfo(host, service, service.Description); // Parse existing info
                }

                HostUpdated?.Invoke(host);
            }
        }

        /// <summary>
        /// Probes UDP Port 137 (NetBIOS Name Service) to request the NetBIOS name table.
        /// </summary>
        /// <param name="ip">The target IP address.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A string containing parsed NetBIOS names or an error message.</returns>
        private async Task<string> ProbeNetBiosUdp137(string ip, CancellationToken ct)
        {
            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = 3000;
            udpClient.Client.SendTimeout = 3000;

            try
            {
                // NetBIOS Name Service Node Status Request (Opcode 0x0A, QDCount 1, Name: '*' for all names)
                // This packet asks for the name table of the target host.
                byte[] requestPacket = new byte[]
                {
                    0x80, 0x01, // Transaction ID (arbitrary, can be random)
                    0x00, 0x00, // Flags (0x0000 = standard query)
                    0x00, 0x01, // QDCount (1 query)
                    0x00, 0x00, // ANCount (0 answer RRs)
                    0x00, 0x00, // NSCount (0 authority RRs)
                    0x00, 0x00, // ARCount (0 additional RRs)
                    // Question section:
                    0x20, // Length of next part (32 bytes)
                    0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, // CKAAAAAAAAAAAAAAAA - Encoded '*' (wildcard)
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                    0x00, // Null terminator
                    0x00, 0x21, // Type: NBSTAT (NetBIOS Node Status)
                    0x00, 0x01  // Class: IN (Internet)
                };

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), 137);

                // Send the request
                await udpClient.SendAsync(requestPacket, requestPacket.Length, remoteEndPoint);

                // Receive the response
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                {
                    try
                    {
                        var receiveTask = udpClient.ReceiveAsync();
                        var completedTask = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

                        if (completedTask == receiveTask)
                        {
                            var result = await receiveTask;
                            byte[] responseBytes = result.Buffer;

                            if (responseBytes.Length > 56) // Minimum size for a useful NetBIOS response
                            {
                                // Parse the NetBIOS name table response
                                return ParseNetBiosNameTableResponse(responseBytes);
                            }
                            else
                            {
                                return "NetBIOS (UDP 137): Received small/empty response.";
                            }
                        }
                        else // Timeout
                        {
                            return "Error: NetBIOS (UDP 137) probe timed out.";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return "Error: NetBIOS (UDP 137) probe cancelled.";
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode == SocketError.TimedOut)
                    {
                        return "Error: NetBIOS (UDP 137) probe timed out (SocketException).";
                    }
                    catch (Exception ex)
                    {
                        return $"Error: NetBIOS (UDP 137) probe failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error: NetBIOS (UDP 137) setup failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Probes TCP Port 139 (NetBIOS Session Service / SMB over NetBIOS) with a minimal SMB Negotiate.
        /// </summary>
        /// <param name="ip">The target IP address.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A string indicating SMB presence or an error message.</returns>
        private async Task<string> ProbeNetBiosTcp139(string ip, CancellationToken ct)
        {
            using var tcpClient = new TcpClient();
            tcpClient.ReceiveBufferSize = 4096;
            tcpClient.SendTimeout = 3000;
            tcpClient.ReceiveTimeout = 3000;

            try
            {
                var connectTask = tcpClient.ConnectAsync(ip, 139);
                var timeoutTask = Task.Delay(3000, ct);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && tcpClient.Connected)
                {
                    using var stream = tcpClient.GetStream();

                    // NetBIOS Session Service (NBT) requires a NetBIOS session header.
                    // This is followed by an SMB Negotiate Protocol request (same as in MSRPC step for 445).
                    byte[] smbNegotiateRequest = new byte[]
                    {
                        0x00, 0x00, 0x00, 0x85, // NetBIOS session service header (Session Message, 0x00, length of SMB message)
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
                            return "Error: NetBIOS (TCP 139) probe timed out or cancelled.";
                        }
                    }

                    string hexResponse = responseBuilder.ToString();
                    if (string.IsNullOrEmpty(hexResponse))
                    {
                        return "Error: No NetBIOS (TCP 139) response received.";
                    }

                    // Look for SMB header (FF-53-4D-42) in the response
                    if (hexResponse.Contains("FF-53-4D-42"))
                    {
                        return $"NetBIOS Session (TCP 139) - SMB Detected. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}...";
                    }
                    else
                    {
                        return $"Port 139 responded, but no SMB signature. Response: {hexResponse.Substring(0, Math.Min(hexResponse.Length, 200))}...";
                    }
                }
                else
                {
                    return "Connection/Timeout error during NetBIOS (TCP 139) probe.";
                }
            }
            catch (Exception ex)
            {
                return $"Error during NetBIOS (TCP 139) probe: {ex.Message}";
            }
        }


        /// <summary>
        /// Parses the raw byte response from a NetBIOS Name Table (NBSTAT) request.
        /// </summary>
        /// <param name="responseBytes">The raw byte array received from UDP/137.</param>
        /// <returns>A formatted string with parsed NetBIOS names and flags.</returns>
        private string ParseNetBiosNameTableResponse(byte[] responseBytes)
        {
            try
            {
                // Basic validation: Check if it's long enough and has a positive number of names.
                if (responseBytes.Length < 56 || responseBytes[56] == 0x00) // 56 is the offset to the name count byte
                {
                    return "NetBIOS (UDP 137) response: Malformed or no names found.";
                }

                int nameCount = responseBytes[56];
                StringBuilder result = new StringBuilder($"NetBIOS Name Table ({nameCount} names):");
                int offset = 57; // Start of the first name entry

                for (int i = 0; i < nameCount; i++)
                {
                    if (offset + 18 > responseBytes.Length) // 16 bytes for name, 1 byte for type, 1 byte for flags
                    {
                        result.Append(" | Response truncated.");
                        break;
                    }

                    string netBiosName = Encoding.ASCII.GetString(responseBytes, offset, 15).TrimEnd(' ');
                    byte nameType = responseBytes[offset + 15]; // Last byte is the name type
                    byte flags = responseBytes[offset + 16];    // Flags (e.g., Group, Deregistered)

                    string typeDescription = GetNetBiosNameTypeDescription(nameType);
                    string flagsDescription = GetNetBiosFlagsDescription(flags);

                    result.Append($" | Name: '{netBiosName}' ({typeDescription}, {flagsDescription})");
                    offset += 18; // Move to the next name entry
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Error parsing NetBIOS name table: {ex.Message} | Raw Hex: {BitConverter.ToString(responseBytes)}";
            }
        }

        private string GetNetBiosNameTypeDescription(byte type)
        {
            // Common NetBIOS name types (last byte of the 16-byte name)
            // https://www.iana.org/assignments/netbios-name-flags/netbios-name-flags.xhtml
            return type switch
            {
                0x00 => "Workstation Service",
                0x03 => "Messenger Service",
                0x06 => "RAS Server Service",
                0x1B => "Domain Master Browser",
                0x1C => "Domain Controller",
                0x1D => "Master Browser",
                0x1E => "Browser Service Elections",
                0x20 => "File Server Service",
                0x21 => "RAS Client Service",
                0xBE => "Network Monitor Agent",
                0xBF => "Network Monitor AFA",
                0x87 => "MS Exchange MTA", // Example
                0x89 => "MS Exchange IMC", // Example
                _ => $"Type: 0x{type:X2}"
            };
        }

        private string GetNetBiosFlagsDescription(byte flags)
        {
            List<string> flagList = new List<string>();
            if ((flags & 0x80) != 0) flagList.Add("Group"); // G: Group name
            if ((flags & 0x40) != 0) flagList.Add("Deregistered"); // D: Deregistered name
            // Add other flags if needed, e.g., unique, registered, etc. (often part of 0x04, 0x08, 0x10)
            return flagList.Any() ? string.Join(",", flagList) : "Unique"; // Default to Unique if no group flags set
        }


        /// <summary>
        /// Parses NetBIOS probe responses to update Host and Service properties.
        /// </summary>
        /// <param name="host">The host object to update.</param>
        /// <param name="service">The service object to update.</param>
        /// <param name="response">The raw NetBIOS probe response string.</param>
        private void ParseNetBiosInfo(Host host, Service service, string response)
        {
            string currentServiceName = service.ServiceName.ToLower();
            string newServiceSuffix = null;

            if (service.Port == 137)
            {
                if (response.Contains("NetBIOS Name Table", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "NetBIOS Name Service (NBNS) - Active";

                    // Try to extract computer name and domain/workgroup
                    var computerNameMatch = System.Text.RegularExpressions.Regex.Match(response, @"Name: '([^']+)' \(Workstation Service,");
                    if (computerNameMatch.Success && string.IsNullOrEmpty(host.Hostname))
                    {
                        host.Hostname = computerNameMatch.Groups[1].Value.Trim();
                        _progressLog.Enqueue($"      Host Hostname set to: {host.Hostname} (from NetBIOS)");
                    }

                    var domainMatch = System.Text.RegularExpressions.Regex.Match(response, @"Name: '([^']+)' \((Domain Controller|Master Browser|Browser Service Elections|Domain Master Browser),");
                    if (domainMatch.Success && string.IsNullOrEmpty(host.Domain))
                    {
                        host.Domain = domainMatch.Groups[1].Value.Trim();
                        _progressLog.Enqueue($"      Host Domain set to: {host.Domain} (from NetBIOS)");
                    }
                }
                else
                {
                    newServiceSuffix = "NetBIOS Name Service (NBNS) - No Table";
                }
            }
            else if (service.Port == 139)
            {
                if (response.Contains("SMB Detected", StringComparison.OrdinalIgnoreCase))
                {
                    newServiceSuffix = "NetBIOS Session Service (NBT/SMB) - Active";
                }
                else
                {
                    newServiceSuffix = "NetBIOS Session Service (NBT/SMB) - No SMB";
                }
            }

            if (!string.IsNullOrEmpty(newServiceSuffix))
            {
                if (currentServiceName == "port " + service.Port || currentServiceName == "netbios" || !currentServiceName.Contains(newServiceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    service.ServiceName = $"NetBIOS {newServiceSuffix}";
                    _progressLog.Enqueue($"      Service Name updated for {host.NetworkInterfaces.First().Ip_Address.First()}:{service.Port} to: {service.ServiceName}");
                }
            }

            // NetBIOS doesn't directly provide Manufacturer/Model for hardware.
            // It can strongly indicate Windows OS, but that's already handled by MSRPC/SMB if applicable.
            if ((service.Port == 137 && response.Contains("Workstation Service")) || (service.Port == 139 && response.Contains("SMB Detected")))
            {
                if (string.IsNullOrEmpty(host.Manufacturer)) host.Manufacturer = "Microsoft";
                if (string.IsNullOrEmpty(host.Model)) host.Model = "Windows OS"; // Generic Windows
            }
        }
    }
}