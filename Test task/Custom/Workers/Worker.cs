using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;

using WebSitePerfomanceTool.Entities;

using Test_task.Custom.SignalR;
using Test_task.DataBases;
using Test_task.Custom.CustomEventArgs;


namespace Test_task.Custom.Workers {
    public class Worker {

        private static ConcurrentDictionary<string, Worker> _workers = new ConcurrentDictionary<string, Worker>();

        public static int Count { get; private set; }

        public static Worker Instance(string url, string OwnerKey, out string status) {
            Uri currentUri;
            if(!Uri.TryCreate(url, UriKind.Absolute, out currentUri)) {
                status = "Incorrect url";
                return null;
            }
            status = "";
            if(_workers.ContainsKey(currentUri.Host)) {
                return _workers[currentUri.Host];
            } else {
                var worker = new Worker(currentUri, OwnerKey);
                _workers[currentUri.Host] = worker;
                return worker;
            }
        }

        private Object _syncRootQueue = new Object();
        private Queue<Uri> _pages = new Queue<Uri>();
        private HashSet<Uri> _allFoundPages = new HashSet<Uri>();

        public event EventHandler<EventArgs> Started;
        public event EventHandler<EventArgs> Stopped;
        public event EventHandler<string> NewTestMessage;
        public event EventHandler<PageStateChangedEventArgs> PageStateChanged;
        public event EventHandler<PageAnalyzeCompletedEventArgs> PageAnalyzeCompleted;

        public Uri RootUrl { get; private set; }
        public bool WorkInProgress { get; private set; } = true;
        public string OwnerKey { get; private set; }
        public int PagesChecked { get; private set; }

        public int RequestsPerPage { get; set; } = 5;

        private Worker(Uri url, string ownerKey) {
            var host = url.Host;
            if(host.StartsWith("www."))
                host = host.Substring(4);
            RootUrl = new Uri(url.Scheme + "://" + host + url.PathAndQuery);
            OwnerKey = ownerKey;
            _pages.Enqueue(RootUrl);
            _allFoundPages.Add(RootUrl);
        }

        public void Start() {
            OnWorkerStarted();
            ThreadPool.QueueUserWorkItem((object state) => {
                DoWork();
            });
        }

        public bool CheckOwner(string userKey) {
            return userKey == OwnerKey;
        }

        public void Close() {
            WorkInProgress = false;
        }

        private void OnWorkerStarted() {
            Started?.Invoke(this, null);
        }

        private void OnWorkerStopped() {
            Worker res;
            _workers.TryRemove(RootUrl.Host, out res);
            _pages.Clear();
            _allFoundPages.Clear();
            Stopped?.Invoke(this, null);
        }

        private void OnNewTestMessage(string text) {
            NewTestMessage?.Invoke(this, text);
        }

        private void OnPageStateChanged(PageStateChangedEventArgs args) {
            PageStateChanged?.Invoke(this, args);
        }

        private void OnPageAnalyzeCompleted() {
            PageAnalyzeCompleted?.Invoke(this, new PageAnalyzeCompletedEventArgs());
        }

        private bool IsEqualHosts(string host0, string host1) {
            if(host0.StartsWith("www."))
                host0 = host0.Substring(4);
            if(host1.StartsWith("www."))
                host1 = host1.Substring(4);
            return host0.Equals(host1);
        }

        private Task ReadUrls(string text, Uri rootUri) { // Should I use ~/sitemap.xml? Regex?
            return Task.Factory.StartNew(() => {
                int idStart = 0;
                while(true) {
                    idStart = text.IndexOf("href=\"", idStart);
                    if(idStart == -1)
                        return;
                    idStart += 6;
                    int idFinish = text.IndexOf("\"", idStart);
                    if(idFinish == -1)
                        return;
                    var url = text.Substring(idStart, idFinish - idStart);
                    Uri currentUri;
                    if(Uri.IsWellFormedUriString(url, UriKind.Absolute)) {
                        currentUri = new Uri(url, UriKind.Absolute);
                    } else if(Uri.IsWellFormedUriString(url, UriKind.Relative)) {
                        currentUri = new Uri(rootUri, url);
                    } else {
                        continue;
                    }
                    if(currentUri.Host.IndexOf(RootUrl.Host) != -1) {
                        if(!_allFoundPages.Contains(currentUri)) {
                            _allFoundPages.Add(currentUri);
                            lock(_syncRootQueue) {
                                _pages.Enqueue(currentUri);
                            }
                        }
                    }
                }
            });
        }

        private string SendRequest(Uri target, bool ignoreContent, out long time) {
            HttpWebRequest request = HttpWebRequest.Create(target) as HttpWebRequest;
            request.Accept = "text/html";
            time = DateTime.Now.Ticks;
            string content = null;
            using(var responce = request.GetResponse()) {
                time = DateTime.Now.Ticks - time;
                if(responce.ContentType.IndexOf("text/html") == -1) {
                    return "";
                }
                if(!ignoreContent) {
                    using(StreamReader sr = new StreamReader(responce.GetResponseStream())) {
                        content = sr.ReadToEnd();
                    }
                }
            }
            return content;
        }

        private void DoWork() {
            var dbMain = TestsContext.Instance();
            var root = new Test() {
                RootHost = RootUrl.Host,
                TimeStart = DateTime.Now.Ticks
            };
            dbMain.Tests.Add(root);
            while(_pages.Count != 0 && WorkInProgress && PagesChecked < 10000) {
                Uri currentUri;
                lock(_syncRootQueue) {
                    currentUri = _pages.Dequeue();
                }
                List<float> results = new List<float>(RequestsPerPage);
                long time = 0;
                string content = "";
                try {
                    content = SendRequest(currentUri, false, out time);
                } catch(WebException exception) {
                    var response = exception.Response as HttpWebResponse;
                    if(response != null) {
                        OnPageStateChanged(new PageStateChangedEventArgs() {
                            MinTime = 99999,
                            AvgTime = 99999,
                            MaxTime = 99999,
                            CurrentUrl = currentUri.ToString(),
                            Status = (int)response.StatusCode
                        });
                        root.PageInfos.Add(new Page() {
                            AvgTime = 99999,
                            MaxTime = 99999,
                            MinTime = 99999,
                            PageUrl = currentUri.ToString(),
                            Result = root,
                            Status = (int)response.StatusCode
                        });
                        dbMain.SaveChanges();
                    }
                    OnPageAnalyzeCompleted();
                    PagesChecked++;
                    continue;
                } catch(Exception) {
                    continue;
                }
                if(string.IsNullOrEmpty(content))
                    continue;
                var urlsReader = ReadUrls(content, currentUri);
                results.Add((float)time / TimeSpan.TicksPerMillisecond);
                for(int i = 1; i < RequestsPerPage && WorkInProgress; i++) {
                    try {
                        content = SendRequest(currentUri, false, out time);
                    } catch(WebException) { // for DOS filters
                        for(int j=0;j<100 && WorkInProgress; j++) // in case client want to abort job but dont wanna wait 10 secs
                            Thread.Sleep(100);
                        continue;
                    } catch(Exception) {
                        continue;
                    }
                    results.Add((float)time / TimeSpan.TicksPerMillisecond);
                    OnPageStateChanged(new PageStateChangedEventArgs() {
                        MinTime = results.Min(),
                        AvgTime = results.Average(),
                        MaxTime = results.Max(),
                        CurrentUrl = currentUri.ToString(),
                        Status = 200
                    });
                }
                OnPageAnalyzeCompleted();
                root.PageInfos.Add(new Page() {
                    AvgTime = results.Average(),
                    MaxTime = results.Max(),
                    MinTime = results.Min(),
                    PageUrl = currentUri.ToString(),
                    Result = root,
                    Status = 200
                });
                dbMain.SaveChanges();
                PagesChecked++;
                urlsReader.Wait();
            }
            root.TimeStop = DateTime.Now.Ticks;
            dbMain.SaveChanges();
            OnWorkerStopped();
        }


    }
}