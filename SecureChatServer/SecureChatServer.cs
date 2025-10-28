using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Serilog;

public class SecureChatServer
{
    private const int Port = 5000;
    private const string CertPath = "securechat.pfx";
    private const string CertPassword = "password123";
    private const string UploadDir = "ServerUploads";

    private static readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private static readonly ConcurrentDictionary<string, ChatMessage> _messages = new();


    public static async Task StartServer()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("server.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            if (!File.Exists(CertPath))
            {
                Console.WriteLine($"❌ Không tìm thấy chứng chỉ: {CertPath}");
                return;
            }

            Directory.CreateDirectory(UploadDir);

            var cert = new X509Certificate2(CertPath, CertPassword, X509KeyStorageFlags.MachineKeySet);
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();

            Log.Information("🚀 Server đang chạy trên cổng {Port}", Port);

            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client, cert);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Lỗi khi khởi động server: {Message}", ex.Message);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task HandleClientAsync(TcpClient client, X509Certificate2 cert)
    {
        string clientId = Guid.NewGuid().ToString();
        string clientName = "Guest_" + clientId[..4];
        SslStream? ssl = null;

        try
        {
            ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsServerAsync(cert, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            var conn = new ClientConnection(clientId, clientName, ssl, client);
            _clients.TryAdd(clientId, conn);

            Console.WriteLine($"✅ {clientName} đã kết nối.");
            await BroadcastMessageAsync($"[INFO] {clientName} đã tham gia phòng chat.", conn);

            await ReceiveLoopAsync(conn);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Lỗi client {Name}: {Message}", clientName, ex.Message);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            client.Close();
            ssl?.Close();
            await BroadcastMessageAsync($"[INFO] {clientName} đã ngắt kết nối.", null);
        }
    }

    private static async Task ReceiveLoopAsync(ClientConnection clientConn)
    {
        var stream = clientConn.Stream!;
        var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            string? msg;
            try
            {
                msg = await reader.ReadLineAsync();
                if (msg == null) break;

                if (msg.StartsWith("[SET_NAME]:"))
                {
                    await HandleSetName(clientConn, msg);
                }
                else if (msg.StartsWith("[IMG_START]:"))
                {
                    await HandleIncomingImage(clientConn, msg);
                }
                else if (msg.StartsWith("[RECALL_REQ]:"))
                {
                    await HandleRecallMessage(clientConn, msg);
                }
                // ⭐ PHẦN ĐÃ SỬA: Bỏ kiểm tra [MSG]:. Mọi tin nhắn không phải lệnh đều được coi là tin nhắn chat.
                else
                {
                    //  Khi nhận tin nhắn thường, gán ID và lưu lại
                    var chatMsg = new ChatMessage(clientConn.Name, msg);
                    _messages.TryAdd(chatMsg.Id, chatMsg);

                    // Gửi đến tất cả client theo định dạng có ID
                    string formatted = $"[MSG_BROADCAST]:{chatMsg.Id}|{chatMsg.Sender}|{chatMsg.Content}";
                    await BroadcastMessageAsync(formatted, null);
                }
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Lỗi khi nhận dữ liệu từ {Name}", clientConn.Name);
                break;
            }
        }
    }

    private static async Task HandleIncomingImage(ClientConnection sender, string header)
    {
        // [IMG_START]:filename|size|mime
        string[] parts = header.Substring(12).Split('|');
        if (parts.Length < 3) return;

        string fileName = parts[0];
        long fileSize = long.Parse(parts[1]);
        string mime = parts[2];
        string savePath = Path.Combine(UploadDir, GetUniqueFileName(fileName));

        Console.WriteLine($"🖼️ Đang nhận ảnh {fileName} ({fileSize} bytes) từ {sender.Name}");

        try
        {
            byte[] buffer = new byte[8192];
            long total = 0;

            using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
            {
                while (total < fileSize)
                {
                    int read = await sender.Stream!.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, fileSize - total));
                    if (read <= 0) break;

                    await fs.WriteAsync(buffer, 0, read);
                    total += read;
                }
            }

            Console.WriteLine($"✅ Đã nhận ảnh {fileName} từ {sender.Name}. Gửi lại cho các client khác...");

            byte[] data = File.ReadAllBytes(savePath);
            await BroadcastImageAsync(sender, fileName, data, mime);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Lỗi khi nhận ảnh từ {Name}", sender.Name);
        }
    }

    private static async Task BroadcastImageAsync(ClientConnection sender, string fileName, byte[] data, string mime)
    {
        string header = $"[IMG_BROADCAST]:{fileName}|{data.Length}|{mime}\n";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);

        foreach (var client in _clients.Values)
        {
            if (client.Id == sender.Id) continue;

            try
            {
                await client.Stream!.WriteAsync(headerBytes, 0, headerBytes.Length);
                await client.Stream.WriteAsync(data, 0, data.Length);
                await client.Stream.WriteAsync(Encoding.UTF8.GetBytes("\n[IMG_END]\n"));
                await client.Stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "⚠️ Không gửi được ảnh cho {Name}", client.Name);
            }
        }
    }

    private static async Task HandleSetName(ClientConnection client, string msg)
    {
        string name = msg.Substring(11).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            string old = client.Name;
            client.Name = name;
            await BroadcastMessageAsync($"[INFO] {old} đổi tên thành {name}.", null);
        }
    }

    private static async Task BroadcastMessageAsync(string msg, ClientConnection? sender)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(msg + "\n");

        foreach (var client in _clients.Values)
        {
            if (client.Id == sender?.Id) continue;

            try
            {
                await client.Stream!.WriteAsync(bytes, 0, bytes.Length);
                await client.Stream.FlushAsync();
            }
            catch
            {
                // bỏ qua client lỗi
            }
        }
    }
    //  Xử lý lệnh thu hồi tin nhắn
    private static async Task HandleRecallMessage(ClientConnection sender, string msg)
    {
        string msgId = msg.Substring(13).Trim();

        if (_messages.TryRemove(msgId, out var recalledMsg))
        {
            Console.WriteLine($" Tin nhắn {msgId} của {sender.Name} đã được thu hồi.");

            // Gửi lại cho tất cả client khác thông báo thu hồi
            await BroadcastMessageAsync($"[RECALL]:{msgId}", null);
        }
        else
        {
            await SendPrivateAsync(sender, $"[ERROR] Tin nhắn {msgId} không tồn tại hoặc đã bị xóa.");
        }
    }

    //Gửi thông báo riêng cho 1 client
    private static async Task SendPrivateAsync(ClientConnection client, string msg)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg + "\n");
            await client.Stream!.WriteAsync(bytes, 0, bytes.Length);
            await client.Stream.FlushAsync();
        }
        catch { }
    }


    private static string GetUniqueFileName(string name)
    {
        string baseName = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        string result = name;
        int i = 1;

        while (File.Exists(Path.Combine(UploadDir, result)))
        {
            result = $"{baseName}({i++}){ext}";
        }

        return result;
    }
}

public class ClientConnection
{
    public string Id { get; }
    public string Name { get; set; }
    public SslStream? Stream { get; }
    public TcpClient? Client { get; }

    public ClientConnection(string id, string name, SslStream stream, TcpClient client)
    {
        Id = id;
        Name = name;
        Stream = stream;
        Client = client;
    }
}
// Lưu thông tin tin nhắn để có thể thu hồi
public class ChatMessage
{
    public string Id { get; set; }
    public string Sender { get; set; }
    public string Content { get; set; }

    public ChatMessage(string sender, string content)
    {
        Id = Guid.NewGuid().ToString();
        Sender = sender;
        Content = content;
    }
}


public class Program
{
    public static async Task Main()
    {
        await SecureChatServer.StartServer();
    }
}