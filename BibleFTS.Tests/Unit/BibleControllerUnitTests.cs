using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nest;
using BibleFTS.Api.Controllers;
using BibleFTS.Api.Services;
using BibleFTS.Api.Models;

namespace BibleFTS.Tests.Unit;

public class BibleControllerUnitTests
{
    [Fact]
    public async Task Search_Should_Return_400_When_Query_Is_Empty()
    {
        var mockElastic = new Mock<IElasticClient>();
        var seeder = new BibleSeeder(mockElastic.Object);
        var controller = new BibleController(mockElastic.Object, seeder);

        var result = await controller.Search("");
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        badRequest!.Value.Should().Be("Query cannot be empty.");
    }

    [Fact]
    public async Task Search_Should_Return_Empty_When_No_Results_Found()
    {
        var elastic = new Mock<IElasticClient>();
        var seeder = new BibleSeeder(elastic.Object);
        var controller = new BibleController(elastic.Object, seeder);

        var emptyResponse = new Mock<ISearchResponse<Verse>>();
        emptyResponse.SetupGet(r => r.IsValid).Returns(true);
        emptyResponse.SetupGet(r => r.Hits).Returns(new List<IHit<Verse>>());
        emptyResponse.SetupGet(r => r.Documents).Returns(new List<Verse>());
        emptyResponse.SetupGet(r => r.Took).Returns(8);
        emptyResponse.SetupGet(r => r.Total).Returns(0);
        elastic.Setup(x => x.SearchAsync<Verse>(
                It.IsAny<Func<SearchDescriptor<Verse>, ISearchRequest>>(),
                It.IsAny<CancellationToken>()))
          .ReturnsAsync(emptyResponse.Object);

        var result = await controller.Search("God created");
        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().NotBeNull();

        var type = ok.Value!.GetType();
        var totalProp = type.GetProperty("total")!.GetValue(ok.Value);
        var tookProp = type.GetProperty("tookMs")!.GetValue(ok.Value);
        var resultsProp = type.GetProperty("results")!.GetValue(ok.Value) as IEnumerable<object>;

        ((long)totalProp!).Should().Be(0);
        ((long)tookProp!).Should().Be(8);
        resultsProp!.Should().BeEmpty();
    }

    [Fact]
    public async Task AddVerse_Should_Return_400_When_Index_Is_Invalid()
    {
        var elastic = new Mock<IElasticClient>();
        var seeder = new BibleSeeder(elastic.Object);
        var controller = new BibleController(elastic.Object, seeder);

        var mockResponse = new IndexResponse();
        elastic.Setup(x => x.IndexDocumentAsync(
                It.IsAny<Verse>(),
                It.IsAny<CancellationToken>()))
          .ReturnsAsync(mockResponse);

        var verse = new Verse { Book = "Genesis", Chapter = 1, VerseNumber = 1, Text = "..." };
        var result = await controller.AddVerse(verse);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Search_Should_Return_Results_When_Query_Is_Valid()
    {
        var elastic = new Mock<IElasticClient>();
        var seeder = new BibleSeeder(elastic.Object);
        var controller = new BibleController(elastic.Object, seeder);

        var verses = new List<Verse>
        {
            new() { Book = "Genesis", Chapter = 1, VerseNumber = 1, Text = "In the beginning God created the heavens and the earth." },
            new() { Book = "Exodus",  Chapter = 20, VerseNumber = 13, Text = "You shall not kill." }
        };
        var hit1 = new Mock<IHit<Verse>>();
        hit1.SetupGet(h => h.Id).Returns("gen-1-1");
        hit1.SetupGet(h => h.Index).Returns("bible");
        hit1.SetupGet(h => h.Score).Returns(1.23);
        hit1.SetupGet(h => h.Source).Returns(verses[0]);

        var hit2 = new Mock<IHit<Verse>>();
        hit2.SetupGet(h => h.Id).Returns("exo-20-13");
        hit2.SetupGet(h => h.Index).Returns("bible");
        hit2.SetupGet(h => h.Score).Returns(0.98);
        hit2.SetupGet(h => h.Source).Returns(verses[1]);

        var hits = new List<IHit<Verse>> { hit1.Object, hit2.Object };
        var mockResponse = new Mock<ISearchResponse<Verse>>();
        mockResponse.SetupGet(r => r.IsValid).Returns(true);
        mockResponse.SetupGet(r => r.Hits).Returns(hits);
        mockResponse.SetupGet(r => r.Documents).Returns(verses);
        mockResponse.SetupGet(r => r.Took).Returns(5);
        mockResponse.SetupGet(r => r.Total).Returns(hits.Count);
        elastic.Setup(x => x.SearchAsync(
                It.IsAny<Func<SearchDescriptor<Verse>, ISearchRequest>>(),
                It.IsAny<CancellationToken>()))
          .ReturnsAsync(mockResponse.Object);

        var result = await controller.Search("God");
        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().NotBeNull();

        var payload = ok.Value!;
        var t = payload.GetType();
        var totalProp = t.GetProperty("total");
        var tookProp = t.GetProperty("tookMs");
        var resultsProp = t.GetProperty("results");

        totalProp.Should().NotBeNull("the anonymous payload should contain 'total'");
        tookProp.Should().NotBeNull("the anonymous payload should contain 'tookMs'");
        resultsProp.Should().NotBeNull("the anonymous payload should contain 'results'");
        var tookVal = (long)tookProp!.GetValue(payload)!;
        tookVal.Should().Be(5);
        var resultsVal = (IEnumerable<object>)resultsProp!.GetValue(payload)!;
        resultsVal.Count().Should().Be(2);
    }
}
