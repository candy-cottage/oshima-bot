using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Regions;

/// <summary>
/// 主城：铎京城。
/// <para>旧版在 <c>铎京城</c> 里挂了整套商店读写（<c>VisitStore</c> / <c>SaveGlobalStore</c> /
/// <c>AddGoodsToStore</c>，约 250 行）；v3 的商店已经独立成 <c>StoreCatalog</c> + 数据库，
/// 所以这里只保留地区数据本身，不再带商店逻辑。</para>
/// </summary>
public class 铎京城 : OshimaRegion
{
    public 铎京城()
    {
        Id = 0;
        Name = "铎京城";
        Description = "铎京是大陆的科技中枢，是大陆中的一个城邦国家。" +
                      "其首都铎京市是一座具有独立权势的商业城市，以精密机械和能量研究、强大的现代科技与军事实力闻名。";
        Category = "此地";
        Weathers.Add("酷暑", 39);
        Weathers.Add("炙热", 33);
        Weathers.Add("晴朗", 26);
        Weathers.Add("多云", 20);
        Weathers.Add("暴雨", 14);
        Weathers.Add("寒冷", 6);
        Weathers.Add("严寒", -7);
        Weathers.Add("霜冻", -18);
        ChangeRandomWeather();
    }
}
