using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace WebSitePerfomanceTool.Entities {
    public class Test {
        [Key]
        public int Id { get; set; }
        [Required, MaxLength(128)]
        public string RootHost { get; set; }
        [Required]
        public long TimeStart { get; set; }

        public long TimeStop { get; set; }

        public virtual ICollection<Page> PageInfos { get; set; } = new List<Page>();
    }

    public class Page {
        [Key]
        public int Id { get; set; }
        [Required, MaxLength(2048)]
        public string PageUrl { get; set; }
        [Required]
        public int Status { get; set; }
        [Required]
        public float MinTime { get; set; }
        [Required]
        public float MaxTime { get; set; }
        [Required]
        public float AvgTime { get; set; }

        public virtual Test Result { get; set; }
    }

    //<th>Start URL</th>
    //<th>Pages count</th>
    //<th>Errors count</th>
    //<th>Average response time</th>

    public class TestResult {
        public int Id { get; set; }
        public string Url { get; set; }
        public int PagesCount { get; set; }
        public int ErrorsCount { get; set; }
        public float? AvgTime { get; set; }
    }

}
