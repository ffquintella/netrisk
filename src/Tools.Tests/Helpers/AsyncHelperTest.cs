using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Tools.Helpers;
using Tools.Math;
using Xunit;

namespace Tools.Tests.Helpers;

[TestSubject(typeof(AsyncHelper))]
public class AsyncHelperTest
{
    [Fact]
    public void RunSyncTest()
    {
        var result = AsyncHelper.RunSync(() => Task.FromResult(5));
        Assert.Equal(5, result);
    }
    
    [Fact]
    public void RunSyncTest2()
    {
        var result = AsyncHelper.RunSync(() => Task.FromResult(5), CancellationToken.None);
        Assert.Equal(5, result);
    }
    
    [Fact]
    public void RunSyncTest3()
    {
        AsyncHelper.RunSync(() => Task.CompletedTask);
    }
    
    [Fact]
    public void RunSyncTest4()
    {
        AsyncHelper.RunSync(() => Task.CompletedTask, CancellationToken.None);
    }
}