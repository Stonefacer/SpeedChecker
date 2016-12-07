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

        public string ConnectionId { get; private set; }

        public User(string connectionId, string userKey) {
            _hub = GlobalHost.ConnectionManager.GetHubContext<SessionHub>();
            ConnectionId = connectionId;
            UserKey = userKey;
        }

        public void AddTestMessage(string message) {
            _hub.Clients.Client(ConnectionId).addTestMessage(message);
        }

        public void SendErrorMessage(string message) {
            _hub.Clients.Client(ConnectionId).sendErrorMessage(message);
        }

        // 0 - ready for action
        // 1 - job in progress
        public void SetGuiState(int state) {
            dynamic data = new ExpandoObject();
            data.url = _worker?.RootUrl.ToString() ?? "";
            _hub.Clients.Client(ConnectionId).setGuiState(state, JsonConvert.SerializeObject(data));
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
            _worker.Start();
        }

        private void _worker_PageAnalyzeCompleted(object sender, CustomEventArgs.PageAnalyzeCompletedEventArgs e) {
            _hub.Clients.Client(ConnectionId).pageAnalyzeCompleted();
        }

        private void _worker_PageStateChanged(object sender, CustomEventArgs.PageStateChangedEventArgs e) {
            _hub.Clients.Client(ConnectionId).updateCurrentState(JsonConvert.SerializeObject(new {
                minTime = e.MinTime.ToString("F02"),
                avgTime = e.AvgTime.ToString("F02"),
                maxTime = e.MaxTime.ToString("F02"),
                statusCode = e.Status,
                currentUrl = e.CurrentUrl
            }));
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
            }
        }

        public void ChangeConnectionId(string newConnectionId) {
            if(ConnectionId == newConnectionId)
                return;
            ConnectionId = newConnectionId;
            //_hub = GlobalHost.ConnectionManager.GetHubContext<SessionHub>();
            SetGuiState(_worker != null ? 1 : 0);
        }

        private void _worker_NewTestMessage(object sender, string e) {
            AddTestMessage(e);
        }

        private void _worker_Stopped(object sender, EventArgs e) {
            SetGuiState(0);
            CloseSession();
            _worker = null;
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