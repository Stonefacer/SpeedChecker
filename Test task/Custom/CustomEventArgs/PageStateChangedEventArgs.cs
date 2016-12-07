using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Test_task.Custom.CustomEventArgs {
    public class PageStateChangedEventArgs:EventArgs {
        public float MinTime { get; set; }
        public float AvgTime { get; set; }
        public float MaxTime { get; set; }
        public int Status { get; set; }
        public string CurrentUrl { get; set; }
    }
}