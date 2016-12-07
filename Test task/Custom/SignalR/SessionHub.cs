using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

using Test_task.Custom.Workers;

namespace Test_task.Custom.SignalR {
    public class SessionHub : Hub {

        //private static HashSet<string> _connections = new HashSet<string>();
        private static ConcurrentDictionary<string, User> _clients = new ConcurrentDictionary<string, User>();
        private static string PrivateSecret = "Пенчекряк";

        private static string CreateNewUserKey(string clientId) {
            return string.Join("", MD5.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(PrivateSecret + DateTime.Now.Ticks.ToString() + clientId))
                .Select(x => x.ToString("X02")));
        }

        private static User GetUser(SessionHub requestHub) {
            string key = requestHub.Context.QueryString["UserKey"];
            if(string.IsNullOrEmpty(key) || !_clients.ContainsKey(key))
                return null;
            else
                return _clients[key];
        }

        public override Task OnConnected() {
            string key = Context.QueryString["UserKey"];
            if(string.IsNullOrEmpty(key)){
                key = CreateNewUserKey(Context.ConnectionId);
                _clients[key] = new User(Context.ConnectionId, key);
                Clients.Caller.setUserKey(key);
            } else {
                if(_clients.ContainsKey(key)) {
                    _clients[key].ChangeConnectionId(Context.ConnectionId);
                } else {
                    _clients[key] = new User(Context.ConnectionId, key);
                    Clients.Caller.setUserKey(key);
                }
            }
            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled) {
            //GetUser(this)?.CloseSession();
            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected() {
            var user = GetUser(this);
            if(user != null) {
                user.ChangeConnectionId(Context.ConnectionId);
            } else {
                var key = CreateNewUserKey(Context.ConnectionId);
                _clients[key] = new User(Context.ConnectionId, key);
                Clients.Caller.setUserKey(key);
                Clients.Caller.setGuiState(0);
            }
            return base.OnReconnected();
        }

        public void Start(string url) {
            var User = GetUser(this);
            if(User == null)
                Clients.Caller.sendErrorMessage("Something went wrong. Please update page and try again.");
            else if(User.HasWorker())
                Clients.Caller.sendErrorMessage("Please cancel all started jobs first.");
            else {
                string result = "";
                if(!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "http://" + url;
                lock(User.syncRoot) {
                    var worker = Worker.Instance(url, User.UserKey, out result);
                    if(worker == null) {
                        Clients.Caller.sendErrorMessage(result);
                    } else {
                        User.AssignWorker(worker);
                    }
                }
            }
        }

        public void Cancel() {
            var User = GetUser(this);
            if(User == null) {
                Clients.Caller.sendErrorMessage("Something went wrong. Please update page and try again.");
            } else if(!User.HasWorker()) {
                User.SetGuiState(0);
            } else {
                User.CloseWorker();
            }
        }

    }
}