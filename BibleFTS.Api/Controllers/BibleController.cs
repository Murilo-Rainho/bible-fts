using Microsoft.AspNetCore.Mvc;
using Nest;
using Elasticsearch.Net;
using BibleFTS.Api.Models;
using System.Text.RegularExpressions;

namespace BibleFTS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BibleController : ControllerBase
    {
        private readonly IElasticClient _elastic;
        public BibleController(IElasticClient elastic)
        {
            _elastic = elastic;
        }

        [HttpPost]
        public async Task<IActionResult> AddVerse([FromBody] Verse verse)
        {
            var response = await _elastic.IndexDocumentAsync(verse);

            if (!response.IsValid)
                return BadRequest(response.OriginalException?.Message ?? "Index error.");

            return Ok(new { id = response.Id, status = response.Result.ToString() });
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query) || Regex.IsMatch(query, @"[^\p{L}\p{N}\s'""-]"))
                return BadRequest("Query cannot be empty.");

            var response = await _elastic.SearchAsync<Verse>(s => s
                .Query(q => q
                    .MultiMatch(mm => mm
                        .Query(query)
                        .Fields(f => f
                            .Field(v => v.Text, boost: 2)
                            .Field(v => v.Book)
                        )
                    )
                )
                .Size(10)
            );

            if (!response.IsValid)
                return StatusCode(500, response.OriginalException?.Message ?? "Search error.");

            var results = response.Hits.Select(h => new
            {
                id = h.Id,
                score = h.Score,
                source = h.Source,
                index = h.Index
            });

            return Ok(new
            {
                total = response.Total,
                tookMs = response.Took,
                results
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteById([FromServices] IElasticClient es, string id)
        {
            var resp = await es.DeleteAsync<Verse>(id, d => d.Index("bible").Refresh(Refresh.True));
            if (!resp.IsValid) return StatusCode(500, resp.OriginalException?.Message ?? "Delete error.");
            return resp.Result == Result.NotFound ? NotFound() : NoContent();
        }

        [HttpDelete("all")] // Use just to dev environment
        public async Task<IActionResult> DeleteAll([FromServices] IElasticClient es)
        {
            var resp = await es.DeleteByQueryAsync<Verse>(d => d
                .Index("bible")
                .Query(q => q.MatchAll())
                .Conflicts(Conflicts.Proceed)
                .Refresh(true)
            );

            if (!resp.IsValid) return StatusCode(500, resp.OriginalException?.Message ?? "DeleteByQuery error.");
            return Ok(new { deleted = resp.Deleted, tookMs = resp.Took });
        }
    }
}