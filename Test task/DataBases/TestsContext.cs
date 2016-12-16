using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Data.Entity;

using WebSitePerfomanceTool.Entities;

namespace Test_task.DataBases {
    public class TestsContext:DbContext {

        public DbSet<Test> Tests { get; set; }
        public DbSet<Page> Pages { get; set; }

        public TestsContext():base(@"Data Source=(localdb)\mssqllocaldb;AttachDbFilename=|DataDirectory|Database.mdf;Integrated Security=True") {
            Database.SetInitializer<TestsContext>(new DropCreateDatabaseIfModelChanges<TestsContext>());
        }

    }
}