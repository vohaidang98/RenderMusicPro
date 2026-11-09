using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RenderMusicPro.Services;

/// <summary>
/// Dò tìm máy chủ kho trên LAN bằng UDP broadcast — để máy NHẬN không cần gõ tay tên máy chủ kho.
/// Máy GỬI (đã bật "chia bài") tự phát 1 gói tin nhỏ mỗi nhịp tim (~5s); máy NHẬN lắng nghe vài giây
/// để liệt kê các máy đang phát. Không liên quan tới cơ chế render (vẫn chỉ dùng thư mục share), đây
/// chỉ là tiện ích tìm tên máy — không gửi/nhận dữ liệu thật qua UDP.
/// </summary>
public static class LanDiscoveryService
{
    private const int Port = 47991;
    private const string Prefix = "RENDERMUSICPRO-LAN:";

    /// <summary>Phát 1 gói quảng bá tên máy này (gọi lại đều đặn từ nhịp tim khi đang bật chia bài).</summary>
    public static async Task AnnounceOnceAsync()
    {
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            byte[] data = Encoding.UTF8.GetBytes(Prefix + Environment.MachineName);
            await udp.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, Port));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Mạng tạm không sẵn sàng (vd vừa đổi Wi-Fi) — bỏ qua, nhịp tim sau tự thử lại.
        }
    }

    /// <summary>Lắng nghe trong khoảng thời gian cho sẵn, trả về danh sách tên máy (khác máy này) đã thấy.</summary>
    public static async Task<List<string>> DiscoverAsync(TimeSpan duration)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            using var cts = new CancellationTokenSource(duration);
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(cts.Token); }
                catch (OperationCanceledException) { break; }
                string text = Encoding.UTF8.GetString(result.Buffer);
                if (!text.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                string name = text[Prefix.Length..];
                if (name.Length > 0 && !string.Equals(name, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Cổng bận / mạng chặn UDP broadcast — để UI báo "không tìm thấy", người dùng gõ tay.
        }
        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
