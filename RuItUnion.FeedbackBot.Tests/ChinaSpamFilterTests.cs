using RuItUnion.FeedbackBot.SpamFilters;

namespace RuItUnion.FeedbackBot.Tests;

public class ChinaSpamFilterTests
{
    private readonly ChinaSpamFilter _filter = new();

    [Fact]
    public void IsSpam_NullOrEmpty_ReturnsFalse()
    {
        string? nullStr = null;
        Assert.False(_filter.IsSpam(nullStr!));
        Assert.False(_filter.IsSpam(string.Empty));
    }

    [Fact]
    public void IsSpam_NoCjkCharacters_ReturnsFalse()
    {
        Assert.False(_filter.IsSpam("Hello world"));
        Assert.False(_filter.IsSpam("Привет мир"));
        Assert.False(_filter.IsSpam("1234567890"));
    }

    [Fact]
    public void IsSpam_CjkRatioExactlyPercent_ReturnsFalse()
    {
        const string text = "你--abcdeft";
        Assert.False(_filter.IsSpam(text));
    }

    [Fact]
    public void IsSpam_CjkRatioAbovePercent_ReturnsTrue()
    {
        const string text = "你我abcd";
        Assert.True(_filter.IsSpam(text));
    }

    [Fact]
    public void IsSpam_NonBmpCjk_IsCountedAsCjk()
    {
        // U+20000 — CJK Extension B
        const string text = "𠀀a";
        Assert.True(_filter.IsSpam(text));
    }

    [Theory]
    [MemberData(nameof(RelevantChineseSpamMessages))]
    public void IsSpam_RelevantChineseAdsFromDataset_ReturnsTrue(string sanitizedMessage)
    {
        Assert.True(_filter.IsSpam(sanitizedMessage));
    }

    public static TheoryData<string> RelevantChineseSpamMessages()
    {
        TheoryData<string> data =
        [
            "子真人饭水1.1%粉红15%到30% 阁主 @упоминание 频道 @упоминание",
            "菜单",
            "❇️ 打破传统索引广告展示模式，开启TG短视频引流新时代\n\n告别传统广告位的生硬曝光，广告原生融入视频内容流，在用户浏览过程中自然呈现，让品牌曝光更真实、点击更主动、转化更高效。\n\n✨ 全新视频信息流广告形态\n\n• 广告展示于视频播放页面，体验更自然\n• 支持频道、群组、品牌商家、联系人等多场景推广\n• 一键跳转频道、联系人或指定链接，缩短转化路径\n• AI 智能匹配兴趣用户，精准触达目标人群\n• 高曝光、高点击、高转化，持续释放品牌价值\n\n🚀 重新定义 Telegram 引流方式。\n\n从传统展示广告升级为内容流推荐，让广告真正融入用户浏览习惯，打造更高效、更精准、更具竞争力的流量入口\n\n👉 [ссылка] 👈",
            "全球号段批发\n低价买号，营销必备\n机器人秒开周/月会员\n3U开月会员\n通知频道 @упоминание\n买号点 @упоминание",
            "波场转账最便宜！手续费仅2TRX/笔\n新用户点击 @упоминание 注册领2笔免费",
            "@упоминание\nTG号出售全网底价\n自助开通月会员",
            "【能量闪租】2TRX=1笔｜4TRX=2笔\n新用户点击 @упоминание 注册领取2笔",
            "TG号批发招代理\n自助开单月会员\n@упоминание",
            "🚀TRX人｜24小时自动处理 @упоминание",
            "你好兄弟  找你有点事情 谈谈 我是一个臭卖号的   我什么都不会 我只会做TG账号  首先我的账号留卡30-90天 可续费手机卡  再也不用怕掉号了  我的账号做了密钥和邮箱加强  私信群发可长期使用 做接粉 接待 精聊号 非常适合   各人使用也是非常适合 主要是不怕官方踢号 之后登录不了    掉号之后直接用手机验证码登录即可  就算你 不粉打 不接待  不做精聊 你也可以做我代理   我的账号非常便宜 都是1-8年的  不买不要紧 进来看看也欢迎  兄弟你肯定会用的上  ☂️ 保存一下联系方式吧   自动卖号机器人  @упоминание   频道  @упоминание 🔥我是私信小号\n\n优点价格非常底 如果你遇到价格比我低的 我会降价",
            "还在担心收款风控，流水太大，暴露身份等问题吗？ \n我司24H 提供支 微 咔 收款金额50-10000   \n \n费用5个点（例如1000X0.95=950回款） \n回款支持 USDT，支付宝，银行卡 \n   \n联系：@упоминание    \n官方频道   [ссылка]",
            "供应商品：飞机号  TG号  会员号\n🇺🇸 +1 美国       🇨🇦 +1 加拿大\n🇨🇱 +56 智利          🇮🇩+62-印尼 \n🇧🇷 +55 巴西          🇭🇰+852-香港\n🇮🇳+91-印度          🇦🇷 +54 阿根廷\n🇹🇷+90土耳其       🇻🇳 +84 越南 \n🇬🇧+44~英国         🇲🇲+95缅甸\n🇰🇭+855~柬埔寨   🇳🇵 +977 尼泊尔\n🇵🇹+351-葡萄牙    🇪🇸 +34 西班牙\n🇱🇻+371-拉脱维亚🇯🇵+81-日本\n🇸🇦 +966 沙特阿拉伯🇺🇿+998-乌兹别克斯坦\n🇦🇫+93-阿富汗      🇨🇿 +420-捷克\n🇸🇻+503-萨尔瓦多 🇩🇴 +1829 多米尼加共和国\n🇺🇸+1-美国实卡    🇨🇦+1-加拿大实卡\n\n🦐新号老号二次号出售价格透明不割韭菜\n（招代理）\n（招代理）\n24小时自助取号  @упоминание\n📣补货通知群： @упоминание",
            "账号销售表\n📣📣点击看价格↓\n\n支持TG各大知名担保🔥\n\n🌟Tiktok ([ссылка])      📱快手账号 ([ссылка])\n\n🌟微信号 ([ссылка])     🌟香港流量卡 ([ссылка])\n\n🌟探探 ([ссылка])        📱📱QQ账号 ([ссылка])\n\n📱whatsapp账号 ([ссылка])\n\n📱face book脸书账号 ([ссылка])\n\n📱苹果id ([ссылка])      📱Telegram账号 ([ссылка])\n\n🌟推特twitter账号 ([ссылка])🌟谷歌邮箱 ([ссылка])\n\n🌟soul账号 ([ссылка])   🌟line账号 ([ссылка])\n\n🌟陌陌 ([ссылка])         📱小红书 ([ссылка])\n\n🌟支付宝 ([ссылка])      \n\n👇\n\n自助购买 ([ссылка])🔵\n\n⭐️⭐️⭐️⭐️⭐️⭐️\n\n📱抖音缺货价格不固定\n只服务老客户",
        ];
        return data;
    }
}