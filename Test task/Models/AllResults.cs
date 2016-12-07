using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ComponentModel.DataAnnotations;

namespace Test_task.Models {
    public class AllResultsModel {
        [Required]
        [MaxLength(50, ErrorMessage = "Name is too long")]
        [MinLength(3, ErrorMessage = "Name it too short. Please tape at least three symbols.")]
        public string Hostname { get; set; }
    }
}