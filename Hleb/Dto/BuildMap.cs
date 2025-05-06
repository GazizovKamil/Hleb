using Hleb.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hleb.Dto
{
    public class BuildMap
    {
        public int fileId { get; set; }
    }

    public class GetInfo
    {
        public int fileId { get; set; }
        public int workerCount { get; set; }
    }

    public class ImportExcel
    {
        public IFormFile file { get; set; }
    }

    public class Clear
    {
        public int fileId { get; set; }
    }
}