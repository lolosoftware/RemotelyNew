using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Helpers;
using Remotely.Shared.Models.Dtos;
using System;
using System.Linq;

namespace Remotely.Shared.Tests;

[TestClass]
public class DtoChunkerTests
{
    [TestMethod]
    public void ReassemblesTextAcrossChunks()
    {
        var chunks = DtoChunker.ChunkDto(new KeyDownDto { Key = "é", Code = "Digit2" }, DtoType.KeyDown, chunkSize: 5).ToArray();
        KeyDownDto? result = null;
        for (var i = 0; i < chunks.Length; i++)
            Assert.AreEqual(i == chunks.Length - 1, DtoChunker.TryComplete(chunks[i], out result));
        Assert.AreEqual("é", result!.Key);
        Assert.AreEqual("Digit2", result.Code);
    }

    [TestMethod]
    public void RejectsMissingFirstChunkAndOutOfOrderChunks()
    {
        var chunks = DtoChunker.ChunkDto(new KeyDownDto { Key = "ArrowLeft" }, DtoType.KeyDown, chunkSize: 2).ToArray();
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(chunks[1], out _));
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(chunks[0], out _));
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(chunks[2], out _));
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(chunks[1], out _));
    }

    [TestMethod]
    public void RejectsOversizedChunksAndMalformedMessagePack()
    {
        var oversized = new DtoWrapper { InstanceId = Guid.NewGuid().ToString(), IsFirstChunk = true,
            IsLastChunk = true, DtoChunk = new byte[50_001] };
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(oversized, out _));
        var invalid = new DtoWrapper { InstanceId = Guid.NewGuid().ToString(), IsFirstChunk = true,
            IsLastChunk = true, DtoChunk = new byte[] { 0xc1 } };
        Assert.IsFalse(DtoChunker.TryComplete<KeyDownDto>(invalid, out _));
    }
}
