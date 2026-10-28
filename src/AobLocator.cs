// ============================================================================
//  BobCore — AOB 定位层（跨版本核心）
// ----------------------------------------------------------------------------
//  职责：把原先散落在 ClBob / ScopeBobSniper 里的硬编码 RVA，改成「签名扫描 →
//        唯一命中 → 反算 RVA」的定位结果。运行期只认签名，不认地址。
//
//  扫描范围：client.dll 的 .text 节（从内存 PE 头解析 VirtualAddress / VirtualSize）
//  判定规则：全 .text 扫描必须恰好 1 处命中；0 处或多处一律判该签名不可用
//           （调用方据此关停对应通道，不写任何字节）。
//
//  签名格式：空格分隔的十六进制字节，"??" 为通配。通配位只出现在跨版本必然变化的
//           相对位移上（call/jmp rel32、rip 相对位移、条件跳转 rel8/rel32 的短跳偏移）。
//  每个签名都带 baselineRva：当前 client.dll(24.10.30) 上的实测 RVA，仅用于打印差值，
//  不参与定位；版本更新后差值不为 0 就是「需要复核」的信号。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BobCore;

internal sealed class SigResult
{
    public readonly string Name;
    public readonly int BaselineRva;
    public readonly byte[] Pat;      // 字节
    public readonly byte[] Mask;     // 1 = 固定，0 = 通配
    public readonly string Display;  // 原始文本

    public bool Scanned;
    public int Hits;
    public bool HitsCapped;
    public int FirstHitRva = -1;
    public int ResolvedRva = -1;
    public string Reason = "未扫描";

    public bool Unique { get { return Scanned && Hits == 1 && ResolvedRva > 0; } }

    public SigResult(string name, int baselineRva, string pattern)
    {
        Name = name;
        BaselineRva = baselineRva;
        Display = pattern;

        string[] parts = pattern.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Pat = new byte[parts.Length];
        Mask = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "??" || parts[i] == "?")
            {
                Pat[i] = 0;
                Mask[i] = 0;
            }
            else
            {
                Pat[i] = Convert.ToByte(parts[i], 16);
                Mask[i] = 1;
            }
        }
    }

    public string HitsText
    {
        get
        {
            if (!Scanned) return "-";
            return HitsCapped ? (">=" + Hits) : Hits.ToString();
        }
    }
}

internal sealed class AobLocator
{
    public const int MaxHits = 16;   // 命中计数上限，够判定「不唯一」即可，避免病态签名拖长时间

    private readonly IntPtr _base;
    private readonly List<SigResult> _sigs = new List<SigResult>();
    private readonly Dictionary<string, SigResult> _map = new Dictionary<string, SigResult>(StringComparer.Ordinal);

    private byte[] _text;
    private int _textRva;
    private int _textLen;

    public bool TextMapped { get { return _text != null; } }
    public int TextRva { get { return _textRva; } }
    public int TextLen { get { return _textLen; } }
    public IList<SigResult> All { get { return _sigs; } }

    public AobLocator(IntPtr baseAddr)
    {
        _base = baseAddr;
        BuildTable();
    }

    // ------------------------------------------------------------ 签名表
    // 来源标注（详见说明文档 §签名表）：
    //   [沿用]   = 直接沿用原插件硬编码字节（原 ENTRY_ORIG / SNAP_ORIG / RENDER_ORIG 等）
    //   [重提]   = 本次针对当前 client.dll 重新提取（原插件无签名，或原签名不足以保证唯一）
    private void BuildTable()
    {
        // ---- 手臂通道（ClBob v1.19.0：5 个 hook 点）
        // [沿用+延长] 原 ENTRY_ORIG 仅 5 字节(48 8B C4 55 53)，全 .text 不唯一；
        //            本次向后延长到指令边界，18 字节固定 + 1 字节观察位。
        Add("arm.entry", 0x4AA730, "48 8B C4 55 53 56 57 48 8D 68 A1 48 81 EC A8 00 00 00 4C");

        // [沿用] 原 SNAP_ORIG 前 10 字节保形，补足到 19 字节并掩掉 rip 位移与 je rel32
        Add("arm.snap", 0x4AADAE, "F3 0F 10 35 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 48 8B 4B");

        // [重提] 原 EXIT_ORIG 只有 5 字节 call rel32（rel32 必须掩，实际只剩 1 字节固定），
        //        本次按调用后语义（mov rdi,rax / test rax,rax / je）延长到 26 字节。
        Add("arm.exit", 0x4AAEF7, "E8 ?? ?? ?? ?? 48 8B F8 48 85 C0 74 ?? 48 8B C8 E8 ?? ?? ?? ?? 84 C0 74 ?? BA");

        // [沿用+延长] 原 ROLL_ORIG 6 字节(FF 90 50 08 00 00) 即 call [rax+0x850]，
        //            本次追加 2 字节后续指令降低碰撞。
        Add("arm.roll", 0x4AB04D, "FF 90 50 08 00 00 40 84");

        // [沿用] 原 SPD_ORIG 5 字节 call rel32 → 掩 rel32 后补足后续 5 字节指令
        Add("arm.spd", 0x4AADD9, "E8 ?? ?? ?? ?? 0F 28 C8 0F 2F");

        // ---- 镜框通道（ScopeBobSniper v1.20.3：1 个 hook 点）
        // [沿用+延长] 原 RENDER_ORIG 7 字节(4C 8B DC 49 89 5B 10) 全 .text 命中 1 处；
        //            本次追加 8 字节（49 89 6B 18 56 57 41 54）降低后续版本误命中概率。
        Add("scope.render", 0xCB1370, "4C 8B DC 49 89 5B 10 49 89 6B 18 56 57 41 54");

        // ---- cvar 注册链（跨版本最易变，三条都必须签名化）
        // [重提] 函数序言特征。CallingConvention 与寄存器保存组合稳定，跨版本存活率高。
        Add("cvar.refinit", 0x110EDB0, "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 B8 FF FF FF FF 49 0F");
        Add("cvar.regcore", 0x110FAE0, "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 55 41 54 41 55 41 56 41 57 48 8D 6C 24 D1");
        Add("cvar.getfloat", 0x110DA0, "40 53 48 83 EC 20 48 8B D9 E8 ?? ?? ?? ?? 48 85 C0 74 ?? F3 0F 10 00 48 83 C4 20 5B C3 48 8B 43 08 48 8B 48 08 F3 0F 10 01 48 83 C4 20 5B C3 CC 4C");

        // ---- v1.33.0 左右手通道：m_bLeftHanded(pawn+0x2400) 唯一读取点 + 6 写入点 + 2 过渡时间戳
        //   全量反汇编确认：读取点只有 1 处（置渲染标志），写入 6 处，过渡时间戳 2 处（hand.snap 与 hand.snapT 相邻）
        Add("hand.render", 0x8ABE78, "80 BE 00 24 00 00 00 74 0A 41 83 4D 20 02");
        // 快照应用：写字段 + 写过渡时间戳（同函数，两段分别 NOP）
        Add("hand.snap", 0x82FAC4, "66 89 B7 00 24 00 00 48 8B 1C C8 89 B7 FC 23 00 00 48 89 B7 04 24 00 00");
        Add("hand.snapT", 0x82FACF, "89 B7 FC 23 00 00 48 89 B7 04 24 00 00");
        // cl_prefer_lefthanded 应用点
        Add("hand.apply", 0x82FE21, "88 87 00 24 00 00 48 8B C7 49 8B E3 41 5F 41 5E 41 5D 41 5C 5F C3");
        Add("hand.apply2", 0x846626, "88 83 00 24 00 00 E8 ?? ?? ?? ?? E9 ?? ?? ?? ??");
        // switchhands
        Add("hand.toggle", 0x868E79, "40 88 BB 00 24 00 00 48 8B 7C 24 30 48 83 C4 20 5B C3");
        // switchhandsleft / switchhandsright
        Add("hand.left", 0x868EEB, "C6 83 00 24 00 00 01 48 83 C4 20 5B C3");
        Add("hand.right", 0x868F5B, "C6 83 00 24 00 00 00 48 83 C4 20 5B C3");
        // 过渡时间戳（+0x23FC）
        Add("hand.deadline", 0x5375B6, "F3 0F 11 80 FC 23 00 00 48 83 C4 20 5F C3");
    }

    private void Add(string name, int baselineRva, string pattern)
    {
        SigResult s = new SigResult(name, baselineRva, pattern);
        _sigs.Add(s);
        _map[name] = s;
    }

    public SigResult Get(string name)
    {
        SigResult s;
        return _map.TryGetValue(name, out s) ? s : null;
    }

    /// <summary>取唯一命中反算出的 RVA；不可用时返回 -1（调用方必须按失败处理）。</summary>
    public int Rva(string name)
    {
        SigResult s = Get(name);
        return (s != null && s.Unique) ? s.ResolvedRva : -1;
    }

    public bool InText(int rva)
    {
        return _text != null && rva >= _textRva && rva < _textRva + _textLen;
    }

    // ------------------------------------------------------------ .text 映射
    /// <summary>解析内存中的 PE 头，把 .text 节整段读进托管数组，供纯托管扫描。</summary>
    public bool MapText()
    {
        try
        {
            int lfanew = Marshal.ReadInt32(_base, 0x3C);
            if (lfanew <= 0 || lfanew > 0x1000) return false;

            short nsec = Marshal.ReadInt16(_base, lfanew + 6);
            short optSize = Marshal.ReadInt16(_base, lfanew + 20);
            if (nsec <= 0 || nsec > 96) return false;

            IntPtr sec0 = _base + lfanew + 24 + optSize;
            for (int i = 0; i < nsec; i++)
            {
                IntPtr sh = sec0 + i * 40;
                byte[] nm = new byte[8];
                Marshal.Copy(sh, nm, 0, 8);
                string name = Encoding.ASCII.GetString(nm).TrimEnd('\0');
                if (name != ".text") continue;

                int vsize = Marshal.ReadInt32(sh, 8);
                int va = Marshal.ReadInt32(sh, 12);
                int rawSize = Marshal.ReadInt32(sh, 16);
                int size = vsize > 0 ? vsize : rawSize;
                if (size <= 0 || size > 0x8000000 || va <= 0) return false;

                byte[] buf = new byte[size];
                Marshal.Copy(_base + va, buf, 0, size);
                _text = buf;
                _textRva = va;
                _textLen = size;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    // ------------------------------------------------------------ 扫描
    public void ScanAll()
    {
        foreach (SigResult s in _sigs) ScanOne(s);
    }

    private void ScanOne(SigResult s)
    {
        s.Scanned = true;
        s.Hits = 0;
        s.HitsCapped = false;
        s.FirstHitRva = -1;
        s.ResolvedRva = -1;

        if (_text == null)
        {
            s.Reason = ".text 不可用";
            return;
        }

        int n = s.Pat.Length;
        int limit = _textLen - n;
        if (limit < 0)
        {
            s.Reason = "签名长度超过 .text";
            return;
        }

        byte first = s.Pat[0];
        int hits = 0;
        int firstHit = -1;
        int i = 0;

        while (true)
        {
            i = Array.IndexOf(_text, first, i);
            if (i < 0 || i > limit) break;

            if (MatchAt(i, s.Pat, s.Mask))
            {
                hits++;
                if (firstHit < 0) firstHit = i;
                if (hits >= MaxHits) { s.HitsCapped = true; break; }
            }
            i++;
        }

        s.Hits = hits;
        if (hits > 0) s.FirstHitRva = _textRva + firstHit;

        if (hits == 0)
        {
            s.Reason = "0 处命中 → 签名不可用";
        }
        else if (hits == 1)
        {
            s.ResolvedRva = s.FirstHitRva;
            s.Reason = "唯一命中";
        }
        else
        {
            s.Reason = (s.HitsCapped ? ">=" : "") + hits + " 处命中 → 不唯一，签名不可用";
        }
    }

    private bool MatchAt(int off, byte[] pat, byte[] mask)
    {
        for (int k = 0; k < pat.Length; k++)
        {
            if (mask[k] != 0 && _text[off + k] != pat[k]) return false;
        }
        return true;
    }

    // ------------------------------------------------------------ 报告
    public string Describe(SigResult s)
    {
        if (s == null) return "(缺失)";
        if (!s.Unique) return s.Reason;
        int delta = s.ResolvedRva - s.BaselineRva;
        return delta == 0 ? "唯一命中，RVA 差值 0" : ("唯一命中，RVA 差值 " + Signed(delta));
    }

    public static string Signed(int v)
    {
        return (v >= 0 ? "+0x" : "-0x") + Math.Abs(v).ToString("X");
    }
}
