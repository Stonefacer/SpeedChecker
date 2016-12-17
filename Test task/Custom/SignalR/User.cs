using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Dynamic;

using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;

using Test_task.Custom.Workers;

namespace Test_task.Custom.SignalR {
    public class User {

        private IHubContext _hub;
        private Worker _worker;

        public readonly Object syncRoot = new Object();
        public string UserKey { get; private set; }

        public LinkedList<string> ConnectionIds { get; private set; }

        public int MaxConnectionPerUser { get; set; } = 10;

        public User(string connectionId, string userKey) {
            _hub = GlobalHost.ConnectionManager.GetHubContext<SessionHub>();
            ConnectionIds = new LinkedList<string>();
            ConnectionIds.AddLast(connectionId);
            UserKey = userKey;
        }

        public void AddTestMessage(string message) {
            foreach(var cid in ConnectionIds) {
                _hub.Clients.Client(cid).addTestMessage(message);
            }
        }

        public void SendErrorMessage(string message) {
            foreach(var cid in ConnectionIds) {
                _hub.Clients.Client(cid).sendErrorMessage(message);
            }
        }

        // 0 - ready for action
        // 1 - job in progress
        public void SetGuiState(int state, string connectionId = "") {
            dynamic data = new ExpandoObject();
            data.url = _worker?.RootUrl.ToString() ?? "";
            if(connectionId == "") {
                foreach(var cid in ConnectionIds)
                    _hub.Clients.Client(cid).setGuiState(state, JsonConvert.SerializeObject(data));
            } else {
                _hub.Clients.Client(connectionId).setGuiState(state, JsonConvert.SerializeObject(data));
            }
        }

        public bool HasWorker() {
            return _worker?.CheckOwner(UserKey) ?? false;
        }

        public void AssignWorker(Worker worker) {
            if(_worker != null)
                throw new InvalidOperationException("other worker already assigned to this object");
            _worker = worker;
            _worker.Started += _worker_WorkerStarted;
            _worker.Stopped += _worker_Stopped;
            _worker.NewTestMessage += _worker_NewTestMessage;
            _worker.PageStateChanged += _worker_PageStateChanged;
            _worker.PageAnalyzeCompleted += _worker_PageAnalyzeCompleted;
            SetGuiState(1);
            if(_worker.CheckOwner(UserKey))
                _worker.Start();
        }

        private void _worker_PageAnalyzeCompleted(object sender, CustomEventArgs.PageAnalyzeCompletedEventArgs e) {
            foreach(var cid in ConnectionIds) {
                _hub.Clients.Client(cid).pageAnalyzeCompleted();
            }
        }

        private void _worker_PageStateChanged(object sender, CustomEventArgs.PageStateChangedEventArgs e) {
            foreach(var cid in ConnectionIds) {
                _hub.Clients.Client(cid).updateCurrentState(JsonConvert.SerializeObject(new {
                    minTime = e.MinTime.ToString("F02"),
                    avgTime = e.AvgTime.ToString("F02"),
                    maxTime = e.MaxTime.ToString("F02"),
                    statusCode = e.Status,
                    currentUrl = e.CurrentUrl
                }));
            }
        }

        public void CloseSession() {
            lock(syncRoot) {
                if(_worker == null)
                    return;
                _worker.Started -= _worker_WorkerStarted;
                _worker.Stopped -= _worker_Stopped;
                _worker.NewTestMessage -= _worker_NewTestMessage;
                _worker.PageStateChanged -= _worker_PageStateChanged;
                _worker.PageAnalyzeCompleted -= _worker_PageAnalyzeCompleted;
                _worker = null;
            }
        }

        public void AddConnectionId(string newConnectionId) {
            if(ConnectionIds.Count >= MaxConnectionPerUser) {
                //SendErrorMessage("Too many connections to the website. Please make sure you are not using same tool in multiply tabs.");
                //return;
                ConnectionIds.RemoveFirst();
            }
            if(!ConnectionIds.Contains(newConnectionId)) // only for low number of connection
                ConnectionIds.AddLast(newConnectionId);
            //_hub = GlobalHost.ConnectionManager.GetHubContext<SessionHub>();
            SetGuiState(_worker != null ? 1 : 0, newConnectionId);
        }

        public void RemoveConnectionId(string connectionId) {
            ConnectionIds.Remove(connectionId);
        }

        private void _worker_NewTestMessage(object sender, string e) {
            AddTestMessage(e);
        }

        private void _worker_Stopped(object sender, EventArgs e) {
            CloseSession();
            _worker = null;
            SetGuiState(0);
            if(ConnectionIds.Count == 0) {
                SessionHub.RemoveUser(UserKey);
            }
        }

        private void _worker_WorkerStarted(object sender, EventArgs e) {
            SetGuiState(1);
        }

        public void CloseWorker() {
            lock(syncRoot) {
                _worker.Close();
            }
        }
    }
}