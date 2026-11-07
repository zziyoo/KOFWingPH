using System.Runtime.CompilerServices;

// 璁拌处鏂规硶锛圕ommitStep / RecordDroppedSteps锛夋晠鎰忎负 internal锛?
// 鍙湁 BattleSession 鍙互鎻愪氦甯э紝澶栭儴绋嬪簭闆嗘棤娉曠粫杩囧畠鐮村潖
// 銆孎rame - 鍒濆甯у彿 == TotalSteps銆嶄笉鍙橀噺銆傛祴璇曢渶瑕佺洿鎺ラ┍鍔ㄨ皟搴﹀櫒锛?
// 鍥犳浠呭娴嬭瘯绋嬪簭闆嗗紑鏀俱€?
[assembly: InternalsVisibleTo("BattleCore.Tests")]

namespace NewKOF.BattleCore;

/// <summary>
/// 鏍稿績妯℃嫙妯″潡鐨勫厓淇℃伅銆?
/// 璇ョ▼搴忛泦涓嶅緱寮曠敤 Godot銆佷笉寰椾緷璧栦换浣曞浘褰㈢獥鍙ｃ€佷笉寰楄鍐欐枃浠剁郴缁熴€?
/// </summary>
public static class BattleCoreInfo
{
    /// <summary>鏍稿績鍐呮牳鐗堟湰銆傝鍒?鏁版嵁鏍煎紡鍙樺寲鏃堕€掑銆?/summary>
    public const int CoreVersion = 2;

    /// <summary>鍥哄畾閫昏緫甯х巼锛圚z锛夈€傛潈濞佹ā鎷熷彧鎸夐€昏緫甯ф帹杩涳紝涓庢覆鏌撳抚鐜囨棤鍏炽€?/summary>
    public const int LogicFrameRate = 60;

    public const string AssemblyName = "NewKOF.BattleCore";
}

