using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Data.Entity;

using WebSitePerfomanceTool.Entities;

namespace Test_task.DataBases {
    public class TestsContext:DbContext {

        private static TestsContext _lastInstance;

        public static TestsContext Instance() {
            //if(_lastInstance == null)
                //_lastInstance = new TestsContext();
            return new TestsContext();
        }

        public DbSet<Test> Tests { get; set; }
        public DbSet<Page> Pages { get; set; }

        private TestsContext():base(@"Data Source=(localdb)\mssqllocaldb;AttachDbFilename=|DataDirectory|Database.mdf;Integrated Security=True") {
            Database.SetInitializer<TestsContext>(new DropCreateDatabaseIfModelChanges<TestsContext>());
        }

    }
}