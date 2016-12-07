using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

using WebSitePerfomanceTool.Entities;

using Test_task.DataBases;
using Test_task.Models;

namespace Test_task.Controllers {
    public class HomeController : Controller {
        public ActionResult Index() {
            var dbMain = TestsContext.Instance();
            var res = dbMain.Tests.Where(x => x.PageInfos.Count(y => y.AvgTime < 99999.00) > 0)
                .OrderByDescending(x => x.TimeStart)
                .Take(100)
                .Select(x => new TestResult() {
                    Id = x.Id,
                    Url = x.RootHost,
                    PagesCount = x.PageInfos.Count,
                    ErrorsCount = x.PageInfos.Where(y => y.Status != 200).Count(),
                    AvgTime = x.PageInfos.Where(y => y.AvgTime < 99999.00).Average(y => y.AvgTime)
                });
            ViewBag.data = res.ToArray();
            return View();
        }

        //public ActionResult About() {
        //    ViewBag.Message = "Your application description page.";
        //    return View();
        //}

        //public ActionResult Contact() {
        //    ViewBag.Message = "Your contact page.";

        //    return View();
        //}

        public ActionResult CheckWebsite() {
            ViewBag.Message = "Check website performance";
            return View();
        }

        [HttpGet]
        public ActionResult AllResults() {
            ViewBag.Message = "Search for test results";
            ViewBag.ShowResults = false;
            return View();
        }

        [HttpPost]
        public ActionResult AllResults(AllResultsModel model) {
            if(!ModelState.IsValid) {
                ViewBag.ShowResults = false;
                return View();
            }
            var dbMain = TestsContext.Instance();
            ViewBag.Message = "Search for test results";
            ViewBag.ShowResults = true;
            var res = dbMain.Tests.Where(x => x.RootHost.IndexOf(model.Hostname) != -1)
                .OrderByDescending(x => x.TimeStart)
                .Select(x => new TestResult() {
                    Id = x.Id,
                    Url = x.RootHost,
                    PagesCount = x.PageInfos.Count,
                    ErrorsCount = x.PageInfos.Where(y => y.Status != 200).Count(),
                    AvgTime = x.PageInfos.Where(y => y.AvgTime < 99999.00).DefaultIfEmpty().Average(y => y.AvgTime)
                });
            ViewBag.data = res.ToArray();
            return View();
        }

        [HttpGet]
        public ActionResult ShowPages(int Id) {
            ViewBag.TestId = Id;
            var dbMain = TestsContext.Instance();
            var test = dbMain.Tests.Where(x => x.Id == Id).FirstOrDefault();
            if(test == null) {
                ViewBag.data = null;
                return View();
            }
            var res = dbMain.Pages.Where(x => x.Result.Id == Id).OrderByDescending(x=>x.AvgTime).Take(1000);
            ViewBag.data = res.ToArray();
            return View();
        }

    }
}