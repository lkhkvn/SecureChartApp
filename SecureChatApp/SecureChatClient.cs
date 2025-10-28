using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Security.Authentication;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Serilog;
using Ookii.Dialogs.Wpf;

public class SecureChatClient
{
    private const string ServerIP = "127.0.0.1"; // có thể đổi thành "localhost" nếu chứng chỉ có CN=localhost
    private const int Port = 5000;
    private const string NameCommandPrefix = "/name ";
    private const string FileCommandPrefix = "/sendfile ";
    private const string DownloadCommandPrefix = "/download ";
    private const string DownloadDir = "Downloads";

    private static SslStream? _sslStream;
    private static CancellationTokenSource _cts = new CancellationTokenSource();

    public static async Task StartClient()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("client.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Directory.CreateDirectory(DownloadDir);

        TcpClient client = new TcpClient();
        try
        {
            await client.ConnectAsync(ServerIP, Port);
            Log.Information("Đã kết nối tới server {ServerIP}:{Port}", ServerIP, Port);

            // Truyền hàm xác thực chứng chỉ đã được chỉnh sửa
            _sslStream = new SslStream(client.GetStream(), false, ValidateServerCertificate, null);

            // Quan trọng: ServerName truyền vào phải trùng CN của chứng chỉ server
            await _sslStream.AuthenticateAsClientAsync("localhost");
            Log.Information("Bắt tay SSL/TLS hoàn tất. Protocol: {Protocol}", _sslStream.SslProtocol);

            var receiveTask = Task.Run(() => ReceiveMessagesAsync(_cts.Token));
            await SendMessagesAsync();
            await receiveTask;
        }
        catch (AuthenticationException ex)
        {
            Log.Fatal(ex, "Lỗi xác thực SSL/TLS: {Message}", ex.Message);
            Console.WriteLine($"\n[ERROR] SSL handshake failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Lỗi Client: {Message}", ex.Message);
            Console.WriteLine($"\n[ERROR] Lỗi kết nối hoặc hoạt động: {ex.Message}");
        }
        finally
        {
            _cts.Cancel();
            _sslStream?.Close();
            client.Close();
            Log.Information("Client đã đóng kết nối.");
            Log.CloseAndFlush();
        }
    }

    // ⭐ ĐÃ SỬA: HÀM CHẤP NHẬN LỖI CHỨNG CHỈ TỰ KÝ (RemoteCertificateChainErrors)
    private static bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // 1. Cho phép nếu không có lỗi nào
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        // 2. CHẤP NHẬN lỗi trong môi trường PHÁT TRIỂN/DEBUG
        // Lỗi RemoteCertificateChainErrors (Chuỗi chứng chỉ lỗi) thường xảy ra với chứng chỉ tự ký.
        if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors) ||
            sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            Console.WriteLine($"[SSL] WARNING: Bỏ qua lỗi SSL (Phát triển): {sslPolicyErrors}");
            // TRẢ VỀ TRUE để chấp nhận kết nối.
            return true;
        }

        // 3. Từ chối đối với các lỗi nghiêm trọng khác (Production)
        Console.WriteLine($"[SSL] ERROR: Chứng chỉ bị từ chối với lỗi: {sslPolicyErrors}");
        return false;
    }
    // ⭐ KẾT THÚC PHẦN ĐÃ SỬA

    private static async Task<string?> ReadMessageLineAsync(SslStream stream, CancellationToken token = default)
    {
        var buffer = new byte[1];
        var byteMessage = new List<byte>();

        while (true)
        {
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, 0, 1, token);
            }
            catch
            {
                return null;
            }

            if (bytesRead == 0)
                return byteMessage.Count > 0 ? Encoding.UTF8.GetString(byteMessage.ToArray()).TrimEnd('\r') : null;

            if (buffer[0] == (byte)'\n')
                return Encoding.UTF8.GetString(byteMessage.ToArray()).TrimEnd('\r');

            byteMessage.Add(buffer[0]);
        }
    }

    private static async Task ReceiveAndSaveFileAsync(SslStream stream, string metadata)
    {
        try
        {
            string data = metadata.Substring("[FILE_TRANSFER]:".Length);
            string[] parts = data.Split('|');

            if (parts.Length != 3 || !long.TryParse(parts[1], out long fileSize))
            {
                Console.WriteLine("\n[ERROR] Metadata file không hợp lệ từ server.");
                return;
            }

            string fileName = parts[0];
            string mimeType = parts[2];
            string tempPath = Path.Combine(Path.GetTempPath(), fileName);

            Console.WriteLine($"\n[DOWNLOAD] Đang nhận file: {fileName} ({fileSize} bytes)...");

            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[8192];
                long received = 0;
                int bytesRead;

                while (received < fileSize)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await fs.WriteAsync(buffer, 0, bytesRead);
                    received += bytesRead;
                }
            }

            Console.WriteLine($"\n[DOWNLOAD] Hoàn tất file {fileName}. Lưu tại thư mục tạm.");

            var saveDialog = new VistaSaveFileDialog
            {
                FileName = fileName,
                Filter = $"{fileName}|*.*",
                DefaultExt = Path.GetExtension(fileName),
                InitialDirectory = Path.GetFullPath(DownloadDir)
            };

            bool? result = saveDialog.ShowDialog();
            if (result == true)
            {
                File.Move(tempPath, saveDialog.FileName, true);
                Console.WriteLine($"[DOWNLOAD] Đã lưu file: {saveDialog.FileName}");
                Process.Start("explorer.exe", Path.GetDirectoryName(saveDialog.FileName)!);
            }
            else
            {
                File.Delete(tempPath);
                Console.WriteLine("[INFO] Hủy lưu file.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Lỗi khi tải file: {ex.Message}");
        }
    }

    private static async Task ReceiveMessagesAsync(CancellationToken token)
    {
        if (_sslStream == null) return;

        try
        {
            string? message;
            while (!token.IsCancellationRequested && (message = await ReadMessageLineAsync(_sslStream, token)) != null)
            {
                if (message.StartsWith("[FILE_TRANSFER]:"))
                {
                    _ = Task.Run(() => ReceiveAndSaveFileAsync(_sslStream, message));
                    continue;
                }

                // Xử lý message có header:
                if (message.StartsWith("[MSG]:"))
                {
                    // [MSG]:<MessageId>|<Sender>|<Content>
                    string data = message.Substring("[MSG]:".Length);
                    var parts = data.Split(new[] { '|' }, 3);
                    if (parts.Length == 3)
                    {
                        string msgId = parts[0];
                        string sender = parts[1];
                        string content = parts[2];
                        Console.WriteLine($"[{sender}] {content} (id={msgId})");
                        // Nếu có GUI: raise event hoặc gọi callback để thêm ChatMessage với MessageId
                    }
                    else
                    {
                        Console.WriteLine("[WARN] MSG header không đúng định dạng: " + message);
                    }
                }
                else if (message.StartsWith("[RECALL]:"))
                {
                    string msgId = message.Substring("[RECALL]:".Length).Trim();
                    Console.WriteLine($"[RECALL] Tin nhắn id={msgId} đã bị thu hồi.");
                    // Nếu có GUI: raise event/call method để cập nhật ChatMessage có MessageId => set IsRecalled = true
                }
                else
                {
                    // fallback: in nguyên dòng nếu không có header
                    Console.WriteLine(message);
                }
            }


        }
        catch (Exception ex)
        {
            Log.Error(ex, "Lỗi khi nhận tin nhắn: {Message}", ex.Message);
        }
    }

    private static async Task SendMessagesAsync()
    {
        if (_sslStream == null) return;

        Console.Write("Nhập tên của bạn: ");
        string? name = Console.ReadLine()?.Trim();

        if (!string.IsNullOrWhiteSpace(name))
        {
            string cmd = $"[SET_NAME]:{name}\n";
            await _sslStream.WriteAsync(Encoding.UTF8.GetBytes(cmd));
        }

        Console.WriteLine("\n--- BẮT ĐẦU CHAT ---");
        Console.WriteLine("Lệnh: /name <tên>, /sendfile <đường dẫn>, /download <file server>");

        while (!_cts.Token.IsCancellationRequested)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();
            if (input == null) continue;

            if (input.StartsWith(FileCommandPrefix))
            {
                string path = input.Substring(FileCommandPrefix.Length).Trim('"');
                if (File.Exists(path))
                    await SendFileAsync(path);
                else
                    Console.WriteLine($"[LỖI] Không tìm thấy file: {path}");
            }
            else
            {
                // Gửi lệnh thu hồi: /recall <MessageId>
                if (input.StartsWith("/recall "))
                {
                    string messageId = input.Substring("/recall ".Length).Trim();
                    if (!string.IsNullOrEmpty(messageId))
                    {
                        // Gửi header thu hồi
                        await SendStructuredMessageAsync($"[RECALL_REQ]:{messageId}");
                        Console.WriteLine($"[INFO] Đã gửi lệnh thu hồi cho MessageId={messageId}");
                    }
                    else
                    {
                        Console.WriteLine("[WARN] Chưa nhập MessageId để thu hồi. Cú pháp: /recall <MessageId>");
                    }
                }
                else
                {
                    // Gửi tin nhắn bình thường với MessageId
                    string messageId = Guid.NewGuid().ToString();
                    string sender = name ?? "Anonymous";
                    string content = input.Replace("\n", " ").Replace("\r", "");
                    // Định dạng: [MSG]:<MessageId>|<Sender>|<Content>
                    string payload = $"[MSG]:{messageId}|{sender}|{content}";
                    await SendStructuredMessageAsync(payload);

                    // Tùy chọn: in ra messageId để người gửi biết (để dễ thu hồi)
                    Console.WriteLine($"[SENT] id={messageId} | {content}");
                }
            }

        }
    }

    private static async Task SendFileAsync(string filePath)
    {
        if (_sslStream == null) return;

        string fileName = Path.GetFileName(filePath);
        long fileSize = new FileInfo(filePath).Length;
        string mimeType = GetMimeType(filePath);

        try
        {
            string header = $"[FILE_START]:{fileName}|{fileSize}|{mimeType}\n";
            await _sslStream.WriteAsync(Encoding.UTF8.GetBytes(header));
            await _sslStream.FlushAsync();

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[8192];
            int bytesRead;
            long sent = 0;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await _sslStream.WriteAsync(buffer, 0, bytesRead);
                sent += bytesRead;
            }

            await _sslStream.FlushAsync();
            Console.WriteLine($"\n[FILE] Đã gửi file {fileName} ({sent} bytes).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Gửi file thất bại: {ex.Message}");
        }
    }

    // Client gui leng thu hoi tin nhan voi server
    private static async Task SendStructuredMessageAsync(string headerContent)
    {
        if (_sslStream == null) return;
        var bytes = Encoding.UTF8.GetBytes(headerContent + "\n");
        await _sslStream.WriteAsync(bytes);
        await _sslStream.FlushAsync();
    }

    private static string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }
}

public class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        await SecureChatClient.StartClient();
    }
}