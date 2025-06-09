using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Orchestrator.WebApi.Controllers
{
    [Route("webdav/{*path}")]
    public class WebDavController : ControllerBase
    {
        private static readonly string Root = Path.Combine(Directory.GetCurrentDirectory(), "..", "webdav-data");

        [AcceptVerbs("GET", "HEAD")]
        public IActionResult GetFile(string? path)
        {
            var fullPath = Path.Combine(Root, path ?? string.Empty);
            if (!System.IO.File.Exists(fullPath))
                return NotFound();
            var fileStream = System.IO.File.OpenRead(fullPath);
            return File(fileStream, "application/octet-stream", enableRangeProcessing: true);
        }

        [HttpPut]
        public async Task<IActionResult> PutFile(string? path)
        {
            if (path == null)
                return BadRequest();
            var fullPath = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var fs = System.IO.File.Create(fullPath);
            await Request.Body.CopyToAsync(fs);
            return Ok();
        }

        [HttpDelete]
        public IActionResult DeleteFile(string? path)
        {
            if (path == null)
                return BadRequest();
            var fullPath = Path.Combine(Root, path);
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
            else if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
            return Ok();
        }

        [AcceptVerbs("MKCOL")]
        public IActionResult MakeCollection(string? path)
        {
            if (path == null)
                return BadRequest();
            var fullPath = Path.Combine(Root, path);
            Directory.CreateDirectory(fullPath);
            return Ok();
        }

        [AcceptVerbs("PROPFIND")]
        public IActionResult PropFind(string? path)
        {
            var fullPath = Path.Combine(Root, path ?? string.Empty);
            if (!Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath))
                return NotFound();

            var depth = Request.Headers["Depth"].ToString();
            var multistatus = new XElement("multistatus",
                new XAttribute(XNamespace.Xmlns + "d", "DAV:"));
            void AddResponse(string p)
            {
                var info = new FileInfo(p);
                multistatus.Add(new XElement("response",
                    new XElement("href", Path.GetRelativePath(Root, p)),
                    new XElement("propstat",
                        new XElement("prop",
                            new XElement("getcontentlength", info.Exists ? info.Length : 0)),
                        new XElement("status", "HTTP/1.1 200 OK"))));
            }

            if (System.IO.File.Exists(fullPath))
            {
                AddResponse(fullPath);
            }
            else
            {
                AddResponse(fullPath);
                if (depth != "0")
                {
                    foreach (var f in Directory.EnumerateFileSystemEntries(fullPath))
                        AddResponse(f);
                }
            }
            var xml = new XDocument(multistatus);
            return Content(xml.ToString(), "application/xml");
        }
    }
}
