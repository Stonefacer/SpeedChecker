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
using System.IO.Compression;
using System.Text;

using WebSitePerfomanceTool.Entities;

using Test_task.Custom.SignalR;
using Test_task.DataBases;
using Test_task.Custom.CustomEventArgs;
using System.Xml;

namespace Test_task.Custom.Workers {
    public class Worker {

        private static ConcurrentDictionary<string, Worker> _workers = new ConcurrentDictionary<string, Worker>();

        public static int Count { get; private set; }

        public static Worker Instance(string url, string OwnerKey, bool force, out string status) {
            Uri currentUri;
            if(!Uri.TryCreate(url, UriKind.Absolute, out currentUri)) {
                status = "Incorrect url";
                return null;
            }
            try {
                if(Dns.GetHostAddresses(currentUri.Host).Length == 0) {
                    status = "Hostname cannot be resolved";
                    return null;
                }
            } catch(Exception) {
                status = "Hostname cannot be resolved";
                return null;
            }
            status = "";
            var host = currentUri.Host;
            if(host.StartsWith("www."))
                host = host.Substring(4);
            var rootUrl = new Uri(currentUri.Scheme + "://" + host + currentUri.PathAndQuery);
            if(_workers.ContainsKey(rootUrl.Host) && _workers[currentUri.Host].Force == force) {
                return _workers[currentUri.Host];
            } else {
                var worker = new Worker(rootUrl, OwnerKey, force);
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
        public bool Force { get; private set; }
        public bool UsedSiteMap { get; private set; } = false;

        public int RequestsPerPage { get; set; } = 5;
        public int MaxPagesCount { get; set; } = 10000;

        private Worker(Uri url, string ownerKey, bool force = false) {
            RootUrl = url;
            OwnerKey = ownerKey;
            _allFoundPages.Add(RootUrl);
            Force = force;
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

        private bool TryFindNextUrl(string text, ref int position, out string result, char quote = '"') {
            result = "";
            if(position >= text.Length)
                return false;
            string startKey;
            if(Force) {
                startKey = quote.ToString();
            } else {
                startKey = "href=" + quote;
            }
            int idStart = text.IndexOf(startKey, position);
            if(idStart == -1)
                return false;
            idStart += Force ? 1 : 6;
            int idFinish = text.IndexOf(quote, idStart);
            if(idFinish == -1)
                return false;
            result = text.Substring(idStart, idFinish - idStart);
            position = idFinish + 1;
            return true;
        }

        private bool TryParseUri(string url, Uri rootUri, out Uri result) {
            if(Uri.IsWellFormedUriString(url, UriKind.Absolute)) {
                return Uri.TryCreate(url, UriKind.Absolute, out result);
            }
            if(Force && Uri.IsWellFormedUriString("http://" + url, UriKind.Absolute)) {
                return Uri.TryCreate("http://" + url, UriKind.Absolute, out result);
            }
            if(Uri.IsWellFormedUriString(url, UriKind.Relative)) {
                return Uri.TryCreate(rootUri, url, out result);
            }
            result = null;
            return false;
        }

        private void AddUri(Uri newUri) {
            if(newUri.Host.IndexOf(RootUrl.Host) != -1) {
                if(!_allFoundPages.Contains(newUri)) {
                    _allFoundPages.Add(newUri);
                    lock(_syncRootQueue) {
                        _pages.Enqueue(newUri);
                    }
                }
            }
        }

        private Task ReadUrls(string text, Uri rootUri) { // Should I use ~/sitemap.xml? Regex?
            return Task.Factory.StartNew(() => {
                int position = 0;
                string url;
                while(TryFindNextUrl(text, ref position, out url)) {
                    Uri currentUri;
                    if(TryParseUri(url, rootUri, out currentUri))
                        AddUri(currentUri);
                }
                position = 0;
                while(TryFindNextUrl(text, ref position, out url, '\'')) {
                    Uri currentUri;
                    if(TryParseUri(url, rootUri, out currentUri))
                        AddUri(currentUri);
                }
            });
        }

        private string SendRequest(Uri target, bool ignoreContent, out long time) {
            HttpWebRequest request = HttpWebRequest.Create(target) as HttpWebRequest;
            if(Force || UsedSiteMap)
                request.Accept = "*/*";
            else
                request.Accept = "text/*; application/*";
            time = DateTime.Now.Ticks;
            string content = null;
            using(var responce = request.GetResponse()) {
                time = DateTime.Now.Ticks - time;
                if(!Force && !UsedSiteMap && responce.ContentType.IndexOf("text/") == -1 && responce.ContentType.IndexOf("application/") == -1) {
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

        private bool TryDownloadASCIIFile(string url, out string result) {
            var request = HttpWebRequest.CreateHttp(url);
            try {
                using(var response = request.GetResponse()) {
                    using(var resStream = response.GetResponseStream()) {
                        if(url.EndsWith(".gz")) {
                            using(var gz = new GZipStream(resStream, CompressionMode.Decompress)) {
                                using(var mem = new MemoryStream()) {
                                    gz.CopyTo(mem);
                                    mem.Seek(0, SeekOrigin.Begin);
                                    using(var sr = new StreamReader(mem, Encoding.ASCII)) {
                                        result = sr.ReadToEnd();
                                        return true;
                                    }
                                }
                            }
                        } else {
                            using(var sr = new StreamReader(resStream, Encoding.ASCII)) {
                                result = sr.ReadToEnd();
                                return true;
                            }
                        }
                    }
                }
            } catch(WebException) {
                result = "";
                return false;
            }
        }

        private bool ReadSitemap(Uri root, string xml, List<Uri> sitemapUrls) {
            try {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);
                var nodesSitemap = xmlDoc.GetElementsByTagName("sitemap");
                foreach(XmlElement el in nodesSitemap) {
                    var loc = el.GetElementsByTagName("loc").OfType<XmlElement>().FirstOrDefault();
                    if(loc == null)
                        continue;
                    Uri currentUri;
                    if(TryParseUri(loc.InnerText, root, out currentUri)) {
                        sitemapUrls.Add(currentUri);
                    }
                }
                var nodes = xmlDoc.GetElementsByTagName("url");
                foreach(XmlElement el in nodes) {
                    var loc = el.GetElementsByTagName("loc").OfType<XmlElement>().FirstOrDefault();
                    if(loc == null)
                        continue;
                    Uri currentUri;
                    if(TryParseUri(loc.InnerText, root, out currentUri)) {
                        AddUri(currentUri);
                    }
                }
                return _pages.Count != 0;
            } catch(Exception) {
                return false;
            }
        }

        private void DownloadSiteMap() {
            var rootUrl = new Uri("http://" + RootUrl.Host);
            string fileBuffer = "";
            var sitemapUrls = new List<Uri>();
            if(TryDownloadASCIIFile(rootUrl + "/robots.txt", out fileBuffer)) {
                var key = "Sitemap:";
                var id = fileBuffer.IndexOf(key);
                while(id != -1) {
                    id += key.Length;
                    var idEnd = fileBuffer.IndexOf('\n', id);
                    Uri sitemapUrl = null;
                    if(idEnd == -1) {
                        if(TryParseUri(fileBuffer.Substring(id).Trim(' ', '\t', '\r', '\n'), RootUrl, out sitemapUrl))
                            sitemapUrls.Add(sitemapUrl);
                        break; // end of file
                    } else {
                        if(TryParseUri(fileBuffer.Substring(id, idEnd - id).Trim(' ', '\t', '\r', '\n'), RootUrl, out sitemapUrl))
                            sitemapUrls.Add(sitemapUrl);
                        id = fileBuffer.IndexOf(key, idEnd);
                    }
                }
            }
            if(sitemapUrls.Count == 0) {
                sitemapUrls.Add(new Uri(rootUrl, "/sitemap.xml"));
            }
            for(int i=0;i<sitemapUrls.Count && _pages.Count < MaxPagesCount; i++) {
                if(TryDownloadASCIIFile(sitemapUrls[i].ToString(), out fileBuffer)) {
                    if(ReadSitemap(rootUrl, fileBuffer, sitemapUrls) && !UsedSiteMap)
                        UsedSiteMap = true;
                }
            }
        }

        private void DoWork() {
            using(var dbMain = new TestsContext()) {
                var root = new Test() {
                    RootHost = RootUrl.Host,
                    TimeStart = DateTime.Now.Ticks
                };
                dbMain.Tests.Add(root);
                if(!Force)
                    DownloadSiteMap();
                if(!UsedSiteMap)
                    _pages.Enqueue(RootUrl);
                while(_pages.Count != 0 && WorkInProgress && PagesChecked < MaxPagesCount) {
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
                        if(!Force)
                            Thread.Sleep(500);
                        continue;
                    } catch(Exception) {
                        continue;
                    }
                    if(string.IsNullOrEmpty(content))
                        continue;
                    Task urlsReader = null;
                    if(!UsedSiteMap)
                        urlsReader = ReadUrls(content, currentUri);
                    results.Add((float)time / TimeSpan.TicksPerMillisecond);
                    for(int i = 1; i < RequestsPerPage && WorkInProgress; i++) {
                        if(!Force)
                            Thread.Sleep(500);
                        try {
                            content = SendRequest(currentUri, false, out time);
                        } catch(WebException) { // for DOS filters
                            for(int j = 0; j < 100 && WorkInProgress; j++) // in case client want to abort job but dont wanna wait 10 secs
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
                    urlsReader?.Wait();
                    if(!Force)
                        Thread.Sleep(1000);
                }
                root.TimeStop = DateTime.Now.Ticks;
                dbMain.SaveChanges();
            }
            OnWorkerStopped();
        }
    }
}