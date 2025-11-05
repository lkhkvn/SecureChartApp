using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using SignalRChatMVC.Models;

namespace SignalRChatMVC.Hubs
{
    public class ChatHub : Hub
    {
        // Danh sách người dùng đang online
        private static ConcurrentDictionary<string, string> Users = new();
        // Danh sách các nhóm chat
        private static ConcurrentDictionary<string, ChatGroup> ChatGroups = new();

        public override async Task OnConnectedAsync()
        {
            var userName = Context.GetHttpContext()?.Request.Query["username"];
            if (!string.IsNullOrEmpty(userName))
            {
                Users[Context.ConnectionId] = userName!;

                // Khi người dùng kết nối lại, kiểm tra và thêm lại vào các nhóm đã tham gia
                foreach (var group in ChatGroups.Values)
                {
                    if (group.Members.Contains(userName!))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, group.Name);
                    }
                }

                await Clients.All.SendAsync("UserJoined", userName);
                await Clients.All.SendAsync("UpdateUserList", Users.Values);
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Users.TryRemove(Context.ConnectionId, out var user))
            {
                await Clients.All.SendAsync("UserLeft", user);
                await Clients.All.SendAsync("UpdateUserList", Users.Values);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string user, string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            await Clients.All.SendAsync("ReceiveMessage", user, message, time);
        }

        public async Task SendPrivateMessage(string toUser, string fromUser, string message)
        {
            var connectionId = Users.FirstOrDefault(u => u.Value == toUser).Key;
            if (connectionId != null)
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                await Clients.Client(connectionId).SendAsync("ReceivePrivateMessage", fromUser, message, time);
            }
        }

        // Tạo nhóm chat mới
        public async Task CreateGroup(string groupName, string description, string createdBy, string? avatar = null, List<string>? initialMembers = null)
        {
            // Kiểm tra tên nhóm
            if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 50)
            {
                throw new HubException("Tên nhóm không hợp lệ");
            }

            // Kiểm tra xem tên nhóm đã tồn tại chưa
            if (ChatGroups.ContainsKey(groupName))
            {
                throw new HubException("Tên nhóm đã tồn tại");
            }

            var group = new ChatGroup
            {
                Name = groupName,
                Description = description,
                Avatar = avatar,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now,
                Members = new List<string> { createdBy },
                Admins = new List<string> { createdBy }
            };

            // Thêm các thành viên ban đầu
            if (initialMembers != null)
            {
                foreach (var member in initialMembers)
                {
                    if (!group.Members.Contains(member) && Users.Values.Contains(member))
                    {
                        group.Members.Add(member);
                    }
                }
            }

            if (ChatGroups.TryAdd(groupName, group))
            {
                // Thêm người tạo vào nhóm
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                // Thêm các thành viên khác vào nhóm
                foreach (var member in group.Members.Where(m => m != createdBy))
                {
                    var connectionId = Users.FirstOrDefault(u => u.Value == member).Key;
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await Groups.AddToGroupAsync(connectionId, groupName);
                        await Clients.Client(connectionId).SendAsync("InvitedToGroup", group);
                    }
                }

                await Clients.All.SendAsync("GroupCreated", group);
            }
        }

        // Tham gia vào nhóm chat
        public async Task JoinGroup(string groupName, string username)
        {
            if (ChatGroups.TryGetValue(groupName, out var group))
            {
                if (!group.Members.Contains(username))
                {
                    group.Members.Add(username);
                    await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                    await Clients.Group(groupName).SendAsync("UserJoinedGroup", username, groupName);
                    await Clients.Client(Context.ConnectionId).SendAsync("JoinedGroup", group);
                }
            }
        }

        // Rời khỏi nhóm chat
        public async Task LeaveGroup(string groupName, string username)
        {
            if (ChatGroups.TryGetValue(groupName, out var group))
            {
                group.Members.Remove(username);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                await Clients.Group(groupName).SendAsync("UserLeftGroup", username, groupName);
            }
        }

        // Gửi tin nhắn trong nhóm
        public async Task SendGroupMessage(string groupName, string user, string message)
        {
            if (ChatGroups.ContainsKey(groupName))
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", groupName, user, message, time);
            }
        }

        // Lấy danh sách nhóm
        public async Task GetGroups()
        {
            await Clients.Caller.SendAsync("ReceiveGroups", ChatGroups.Values);
        }
    }
}
