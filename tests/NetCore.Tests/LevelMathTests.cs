using ARO.NetCore;
using Xunit;

namespace NetCore.Tests
{
    public class LevelMathTests
    {
        [Theory]
        [InlineData(0, 1)] [InlineData(99, 1)] [InlineData(100, 2)] [InlineData(399, 2)] [InlineData(400, 3)] [InlineData(1600, 5)] [InlineData(2499, 5)] [InlineData(2500, 6)]
        public void LevelMatchesTheServerFormula(long xp, int level) => Assert.Equal(level, LevelMath.LevelFor(xp));

        [Fact] public void LevelStartsFollowTheSquareLaw() { Assert.Equal(0, LevelMath.XpAtLevel(1)); Assert.Equal(100, LevelMath.XpAtLevel(2)); Assert.Equal(1600, LevelMath.XpAtLevel(5)); Assert.Equal(2500, LevelMath.XpAtLevel(6)); }

        [Fact] public void ProgressInsideLevelFive()
        {
            var (into, span, f) = LevelMath.Progress(2050);   // level 5 spans 1600..2500
            Assert.Equal(450, into); Assert.Equal(900, span); Assert.Equal(0.5f, f, 3);
        }
        [Fact] public void ProgressAtLevelStartIsZero() { var (into, _, f) = LevelMath.Progress(400); Assert.Equal(0, into); Assert.Equal(0f, f); }
        [Fact] public void NegativeXpIsTreatedAsZero() { Assert.Equal(1, LevelMath.LevelFor(-5)); Assert.Equal(0f, LevelMath.Progress(-5).fraction); }
        [Fact] public void ConsistentWithLevelFor()
        {
            for (long xp = 0; xp < 20000; xp += 37) { int l = LevelMath.LevelFor(xp); Assert.True(xp >= LevelMath.XpAtLevel(l) && xp < LevelMath.XpAtLevel(l + 1)); }
        }
    }
}
