using System;
using System.Threading.Tasks;
using Microsoft.Owin;
using Microsoft.AspNet.SignalR;
using Owin;

[assembly: OwinStartup(typeof(Test_task.startup))]

namespace Test_task {
    public class startup {
        public void Configuration(IAppBuilder app) {
            app.MapSignalR();
        }
    }
}
