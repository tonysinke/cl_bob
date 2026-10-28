---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 236021e9d3b6d35e9035f1c23f28aea0_98c24feed2a511f1b84e52540064ee0f
    ReservedCode1: E/biec/RlkHksBtkeM69JOMczWu8a13cm4lE5rxVTzY7qt73T+1fWcRtOM6PEFwnDZPuE5wiRhNBJUsIB+qnyuPS9TGpQ9aUvUabW6cwDlcA6zOVsaqax78kzJrfjnt3gSKw98r6BbQl6PVhviqhayATfnzXsK3lGXfP8ExPDrFzJKFBc35cl5bLlJ0=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 236021e9d3b6d35e9035f1c23f28aea0_98c24feed2a511f1b84e52540064ee0f
    ReservedCode2: E/biec/RlkHksBtkeM69JOMczWu8a13cm4lE5rxVTzY7qt73T+1fWcRtOM6PEFwnDZPuE5wiRhNBJUsIB+qnyuPS9TGpQ9aUvUabW6cwDlcA6zOVsaqax78kzJrfjnt3gSKw98r6BbQl6PVhviqhayATfnzXsK3lGXfP8ExPDrFzJKFBc35cl5bLlJ0=
---

# 更新日志

> 说明：早期版本（v1.23.5 之前）未在源码头保留记录，本文件从源码注释块抽取生成。

## v1.58.9

- 修复 `cl_bob=1` 实战「抖一会停一会」：相位改为墙钟匀速推进 + 引擎冻结探测。
- 开源版改造：日志/持久化路径不再硬编码，改为 环境变量 `BOBCORE_ROOT` → 服务器根目录 → 插件目录 三级回退。

## v1.58.5（修 v1.58.4 引入的「更抖」：倍率估计改为窗口累积，不再逐帧 d/wallDt）

  · v1.58.4 错在：引擎时间每 tick 才 +15.6ms、波形线程每帧 ~2ms，逐帧相除得 7~8x，
    且只在 d>0 的帧更新 EMA → 倍率被拉到 ~7.6，相位推快 7 倍，反而更抖。
  · 现在：0.2s 窗口内累加引擎时间与墙钟时间再相除，倍率稳定在 ~1.0（demo 倍速才偏离），
    相位按 墙钟帧时间 x 倍率 均匀推进；120ms 引擎零推进才冻结。日志新增 engR 字段可自查。

## v1.58.4（修 1 档「一抖一抖」：相位推进改为 墙钟帧时间 x 引擎时间倍率(EMA) 平滑推进）

  · 现象：幅度已合适，但晃动一顿一顿，不是连续摇摆。
  · 根因：引擎时钟按 tick 阶跃（~64~128Hz），波形线程 ~486Hz；v1.58.0~1.58.3 每帧直接拿引擎时间差
    推进相位 → 跳一帧、停几帧、再跳，形成阶梯推进。
  · 改法：把引擎时间增量换算成「引擎/墙钟」倍率（0.5s EMA），每帧按墙钟帧时间 x 倍率均匀推进；
    连续 120ms 引擎时间零推进（暂停/菜单/加载）才冻结相位；seek/回退只重置基准。
  · 保留 v1.58.3 的 M1_AMP_SCALE=0.50 幅度降档。

## v1.58.3（1 档幅度降一档：vert/lat 源头乘 M1_AMP_SCALE=0.50）

  · 现象：v1.58.2 恢复绝对波形投喂后，实战 cl_bob=1 抖太猛（枪随走动大幅前后甩）。
  · 数据：spd=250 / cl_bobamt_vert=0.2 / cl_bob_lower_amt=15 时内部 vert≈-4.21（lower=4.5 占主导），
    fb=vert*AX_FORWARD≈-1.68 单位，走动时随 speed 在 0↔-1.68 猛甩。
  · 改法：vert/lat 源头统一乘 M1_AMP_SCALE（六通道 + 镜框广播同缩），仅 st==1 生效，2 档/0 档不动。

## v1.58.0（相位时间源改读引擎 curtime —— 修 demo / 观战下晃动不规律与相位漂移）

  · 现象：cl_bob=1 在 demo 回放与观战里晃动节奏忽快忽慢、暂停/快进后相位与画面不同步。
  · 根因：StepWaveArm 的 _bobTime 用 Stopwatch（墙钟）积分。demo 暂停时墙钟照走、相位继续
    推进；demo 快进时墙钟又不跟随倍速 → 相位与引擎时间轴脱钩，越走越偏。
  · 改法（全程进程内直读，不写外部程序、不注入）：
      ＊ 从签名解析出的 arm.spd（0x4AADD9）往前 0x10 现算「取时间」call → 0x18046C5C0；
      ＊ 从该函数体里现算它引用的时钟对象全局（本版 client.dll = RVA 0x182ADF0）；
      ＊ 波形线程每帧读该对象 +0x34 的 float 时间——这正是引擎 bob 函数自己用的本帧时间
        （0x4AADC9 把它写进 [rbp+0x50]，0x4AADD9 再用它算 viewmodel 速度），含 subtick 分数；
      ＊ 相位增量改用引擎 dt：暂停 dt=0（相位冻结）、倍速按倍速、掉帧自动补齐；
        时间回退 / 跳变（换图、demo seek、从头播放）只重置基准，不让相位猛跳。
  · 兜底：任一步校验不过（call 不是 E8、找不到全局、指针为空、数值不像时间）→ 自动退回墙钟，
    其余通道照常工作，不会因为时间源把插件拖挂。

## v1.56.5（cl_bob=2 与游戏内 bob 指令彻底脱钩）

  · 2 档的幅度/周期/上摆/下沉全部取插件后台常量 BG2_*：vert 0.25 / lat 0.40 /
    lower 21 / cycle 0.98 / bobup 0.50。
  · RefreshParamsArm 在 _state==2 时直接返回常量，不再 ReadCvar —— 游戏内改
    cl_bobamt_vert / cl_bobamt_lat / cl_bob_lower_amt / cl_bobcycle / cl_bobup 对 2 档一律无效。
  · 1 档行为不变（仍按游戏内 cvar）；切档时立即按新口径重取一次参数。
  · 日志：apply 行与 beat 的 params 行新增「参数源=后台常量 / cvar」(BG2 / cvar)。

## v1.56.4（cl_bob=2 先完全按 1 档接管）

  · 用户口径：暂不追求 2 档额外去晃，先让 2 档严格等同 1 档的通路与数值。
  · 实现：ApplyStateArm 入口把档位映射 2→1（_effState），此后 _inState / SH_STATE /
    波形步进全走 1 档：快照 + 覆盖 + EXIT 站写回 base，不再触发任何 2 档归零/冻结分支。
  · cvar 原值仍存 _state（日志显示 state=2 eff=1），便于切换对照。

## v1.51.0（修 WA2 cave 的 jmpOff 0x31→0x32：0x31 落在 xorps xmm5 的末字节 0xE5，

          BuildArmCave 自检拒绝构造 → wa2 站从未安装，2 档始终无效）

## v1.50.0（修 WA2 站覆盖长度 5→6：原 5 会让 jmp 落进 movss 中间，实测必崩/失效）

- （见源码注释）

## v1.49.0（cl_bob=2 彻底不晃：补 WA2 站，清直通分支的 pose 累加）

  · 现象：v1.45~v1.48 实测 cl_bob=2 仍原版晃动，beat 日志 wa=0/0 恒不变。
  · 真因：v1.45 的 WA 站挂在 0x4AAF27，位于「0x4AAF04 取到对象且 0x4F4950 返回真」
    这条分支内，运行时一帧都进不去；引擎每帧走的是 0x4AAF41 直通分支，
    xmm3/4/5 直接吃 [rsp+0x30/34/38] 全量 pose，乘相机基向量后累加进
    [rsp+0x40/44/48] → 相机跟着手部走路 pose 晃。
  · 修复：新增 WA2 站 @0x4AAF4D（arm.exit+0x56，`movss xmm3,[rsp+0x30]`，5B），
    2 档时清零 xmm3/xmm4/xmm5，两条分支汇合到 0x4AAF53 的矩阵乘后增量恒为 0。
    原 WA 站（清 xmm6）保留，覆盖另一条分支。仅影响 viewmodel 渲染，不动弹道/视角。

## v1.48.0（恢复 cl_bob_sniper=2 档：偏置站每帧恒写 0，镜框完全静止）

  · 用户反馈 2 档有用，v1.31 整档删除属误删，本版按原语义补回：cvar 上限 1→2。
  · 2 档 = 开闸 + 覆盖值恒 0（SyncBiasDelta 对 st!=1 已算 0），只静止镜框，手臂照常抖。
  · 不恢复任何旧附加参数（_px/_invert/_lower/_bias/_delta/_gain/_amp 仍保持删除）。

## v1.47.0（控制/观战 BOT 时镜框恒不动 = cl_bob_sniper 2 效果 → 观察目标解析修复）

  · 现象：控制 BOT 开镜后镜框一动不动，cl_bob_sniper 10 / cl_bob 12 全无效。
  · 证据：同场 beat.probe 日志恒为 bias gate=1 mode=replace dX=0 dY=0、arm=(0,0)，
      说明 sniper 通道本身开着（sniper=1 生效），只是 bob 源输入恒 0。
  · 根因（反编译 CSS API 1.0.284 元数据确认）：
      1) ObserverServices 不在 CCSPlayerController 上，而在 **Pawn**（CBasePlayerPawn）上，
         旧代码 p.GetType().GetProperty("ObserverServices") 永远 null；
      2) ObserverTarget 的类型是 CHandle<CBaseEntity>，Value 取到的是实体（CCSPlayerPawn），
         旧代码 `as CCSPlayerController` 永远 null。
    → 观察目标解析不出来，PickLocal 退回「观战者自己」；观战者 pawn 无武器、速度 0
      → bob 源恒 0 → 镜框静止，看上去与 cl_bob_sniper=2 一模一样。
  · 改法：
      1) ObserverTargetOf 改从 pawn / ObserverPawn 的 ObserverServices 取 ObserverTarget，
         按实体 handle 回查玩家；兜底 controller.DesiredObserverTarget（CHandle<CEntityInstance>）；
      2) PickLocal 新增 IsObserverPawn / HasWeapon 判据，排除观战者自带 observer pawn，
         候选顺序改为「观察目标 → 有武器真人 → 有武器且最快 → bot → 最快」；
      3) 新增 obs 诊断日志：选择目标变化时打印各玩家的 pawn 类名/是否持枪/观察目标。

## v1.45.0（按用户指令「做」：hook 点下移到 0x4AAF27，2 档在最终变换前清 xmm6，掐断 slot→世界位移的整段 bob 累加）

- （见源码注释）

## v1.44.0（按用户指令：2 档先回归 cl_bob=1 的监管路径，再全数值调 0）

  · v1.43.0 的速度门控实测失败：2 档仍是原版走动晃动，且手臂「一会跟一会不跟」。
  · 「一会跟一会不跟」根因：门控判据是平滑速度 > 0.5，速度在阈值附近来回抖 →
      base 在「实时刷新」与「冻结值」之间反复切换，手臂位置逐帧跳变。
    → 本版把 SNAP 站 0x17 的 jne 改成 jmp（0x75 → 0xEB），门控整段变成不可达死代码，
      base 恢复为与 1 档完全相同的「每帧刷新」，跟手不再断续。
  · 2 档输出仍由 EXIT 站把六槽写回 shared base（= 1 档公式把全部增量取 0 的结果），
      因此 2 档与 1 档走同一条数据通路，只是增量为 0：便于两档直接对照。
  · 波形线程在 state==2 时把 shared 里所有增量字段（SH_FB/SH_BOB/SH_BOB_LR/SH_LOW/
      SH_ROLL/SH_PITCH/SH_YAW）统一写 0 —— 即使 bobacc 跳板漏跳，也不会有任何增量残留。
  · beat 日志新增 base= 六槽实测值，用于判定「数值全 0 后仍晃」是否来自 base 本身（动画层）。

## v1.43.0（cl_bob=2 真凶定位：base 本身就是晃动来源 → 速度门控冻结 base）

  · 现象：v1.42.1 进图不崩，但 cl_bob=2 手臂仍是原版走动晃动。
  · 排查：反汇编 client.dll 0x4AAD00~0x4AB010 后确认整条链路——
      0x4AAD5A call 0x54fa10 算出「viewmodel 基准 pose」写入 [rsp+0x30..0x38]，
      SNAP 站(0x4AADAE) 把它抄进 shared base；0x4AAE4E 段再叠加数值 bob（已被 bobacc 跳过）；
      0x4AAEE3 处 call [rax+0xa70] 的结果会二次覆盖 [rsp+0x30/34/38]，但被 exit 站(0x4AAEF7) 的 base 写回盖掉；
      之后 0x4AAF53 起用相机基向量把 base 变换到世界坐标 —— 视角跟随走这条，与晃动无关。
    → 结论：数值 bob 早已归零，2 档仍晃的原因是 base 每帧都在刷新，而 base 来自 0x54fa10，
      里面带着走/跑动画驱动的骨骼位移。每帧把「含动画晃动的 base」写回 slot，等于把动画晃动搬运到最终 pose。
  · 改法：SNAP 站加门控 —— state==2 且 速度>0.5 时不刷新 base（保留上一次站立姿态），
      exit 站照旧把冻结的 base 写回六槽。走动中手臂完全静止；停下立刻恢复刷新，
      蹲下/换枪/开镜的姿态更新不受影响；转视角由外部相机基向量完成，仍完整跟随。
  · 门控布局：0x12 cmp [r11+0x18],2 / 0x17 jne 0x28 / 0x19 cmp [r11+0x80],0x32 /
      0x24 jbe 0x28 / 0x26 jmp 0x70 / 写块 0x28~0x6F / FRZ 0x70 / CAVE_SNAP_JMP=0xAC，总长 0xB5。
  · shared+0x80 由波形线程每 1ms 写入 (平滑速度 x100)，供门控读取。

## v1.42.1（修复出口 cave rax 占位未补丁导致选边闪退；代表 v1.42.0 改动全保留）

- （见源码注释）

## v1.42.0（撤回左右手改造；2 档改「六槽写回本帧 base」，修 X 轴定住 / 上下视角不跟）

  · 删除 cl_bob_hand 与 cl_hand 两个 cvar 及整条左右手通道：渲染跳板、8 个写入点 NOP、
    手性字段写入重定向、TryGetPawn / DriveHandField / HandTapStats / Schema 偏移解析、
    通道隔离自检里的 hand.render 项全部移除。左右手恢复引擎原生 switchhands 行为。
    旧 cfg 里残留的 cl_bob_hand / cl_hand 进 RemovedCvarNames，加载时自动剔除。
  · cl_bob=2 实测缺陷：v1.41.0 出口站只把 [rsp+0x50]（低值/下压槽）钉成 shared+0x90 静止值，
    结果手臂 X 轴定住、上下晃视角不跟随。原因：六个栈 slot 是「每帧 0x54fa10 重算的基准 pose
    + 原生 bob 累加」，单槽写常数只清一轴、且该轴失去基准。
  · 改法：2 档出口站把 base（SNAP 站每帧刷新的 shared+0x30/0x34/0x38/0x3C/0x58/0x5C）
    原样写回六个 slot —— bob 增量归零、基准姿势与视角跟随完整保留。出口 cave 由 0xE4 扩到 0xED。

## v1.40.0（bob 2 档改直通，彻底去掉 FRZ 钉死；cl_hand 改「引擎反向渲染」真写手性字段）

  · 2 档钉原地根因：v1.39.2 出口站把 [rsp+0x50]/0x54/0x58 覆盖成 FRZ 静止基线（常数），
    这三槽是最终 viewmodel 朝向分量，被钉成常数 → 转视角手臂不跟、观感定死。
    实测 bobacc=65/65（2 档每帧 100% 跳过累加段）已足够实现「不晃且跟随」，
    故 2 档重新回到直通，FRZ 覆盖整段变成不可达死代码。
  · cl_hand：不再只翻渲染标志，直接把 pawn+0x2400(m_bLeftHanded) 写成目标值，
    让引擎本体真反向渲染；8 个写入点仍 NOP（引擎改不回去），仅在被改歪时补写（0.2s 节流）。

## v1.39.3（修复 v1.39.2 出口站 cave 字节错位致进图闪退）

  · 现象：v1.39.1 实测 cl_bob=2 又回到原版晃动；cl_hand 换手仍有“CD 感”。
  · 根因一：bobacc 跳板虽已装（0x4AAE4E → 0x4AAEAD），但 pose 探针里 [rsp+0x50]
    仍在 -1.58↔-4.65 之间颤 —— 该槽的写入还有第二条路径，只靠跳过累加段拦不住；
    而 v1.39.1 出口站对 2 档是「直通」，等于完全不兜底。
  · 根因二：cl_hand 的边沿驱动仍把 pawn+0x2400 写成 1（左手）并清 +0x23FC 冷却戳，
    引擎读到手性字段变更 → 重建 viewmodel / 重播部署动画 → 观感像 CD；
    legacy 的 cl_righthand 只翻转渲染数值，实体字段本身不动，所以零过渡。
  · 改法一（出口站 2 档）：不再直通、也不写 0，改为把 FRZ 静止基线写进三个抖动槽
    [rsp+0x50]/[rsp+0x54]/[rsp+0x58]（对应 LOW / ANG_Y / ANG_Z），位移基值
    0x30/0x34/0x38 原样不动 —— 手臂不钉原地，也不叠加抖动量。
    FRZ_OK==0（基线未就绪）时仍然直通，避免冷启动写脏值。
  · 改法二（bobacc 加计数）：共享区 +0x88/+0x8C 累计「进入 / 真正跳过」次数，
    beat 日志输出 bobacc=进入/跳过，用于确认 2 档是否每帧都跳过了累加段。
  · 改法三（手性）：DriveHandField 彻底不写 pawn 任何字节，只刷渲染跳板 state，
    左右手完全由渲染侧分支镜像（cl_hand 0=左手 走原 left 分支 / 1=右手 跳过），
    等价 legacy 直接镜像、无过渡。

## v1.39.0（换手不再重播“切出”动画：撤销 v1.38.x 对 pawn 手性字段的一切写入）

  · 现象：点切 cl_hand 后画面先镜像、随即又播一遍“把枪切出来”的动画（CS2 换手感），
    legacy 是直接镜像、不过渡。
  · 根因：换手瞬间改写 pawn->m_bLeftHanded(+0x2400)，引擎把它当状态变更，
    viewmodel 走一次切枪/部署过渡；v1.38.0（每 tick 钉值）与 v1.38.1（重定向写入点）
    都会触发，只是触发点不同。
  · 改法：cl_bob_hand=1 时把 m_bLeftHanded 恒钉成 0（右手基准）作为“静态底”，
    6 个写入点 + 2 个时间戳一律 NOP（不再重定向下发），换手只动渲染跳板 state 字节。
    引擎看不到字段变化 → 不重建 viewmodel、不播部署动画；镜像由渲染读点单源决定。
  · 净效果：cl_hand 0/1 绝对赋值，下一帧完成镜像，无过渡动画、无 CD。

## v1.37.1（左右手指令收口：删 cl_hand_toggle / cl_hand_bind（AddCommand 补全即崩，且无用）；

          cl_bob_hand 只保留「隔离引擎换手链路」这一职责；新增 cl_hand（cvar，0=左手 / 1=右手）
          复刻 legacy 的绝对手性切换：立即生效、无 CD、不重切枪，可 bindtoggle 绑键）

## v1.37.0（cl_bob=2 在出口站「接管 + 全调 0」：不再直通、也不再读 base，

          六个 slot 一律写 0 —— 位置(x/y/z)与角度(yaw/z)抖动同时为 0）

## v1.36.0（cl_bob=2 从机制层拆除：SPD 探针把速度强制 0 让原生 comiss/jbe 整段跳过晃动块，出口站 2 档直通不再覆盖

- （见源码注释）

## v1.35.1（2 档改为「还原入口 base」：bob 增量全 0，但 base 仍随视角刷新，不会把胳膊钉在原地）

- （见源码注释）

## v1.35.0（修 cl_bob=2 仍晃：出口站由「加增量」改「绝对覆盖」——2 档在 0x4AAEF7 出口

          直接把六个 slot（0x30/0x34/0x38/0x50/0x54/0x58）写成 FRZ 锁定值，
          原生位置与角度抖动全部被覆盖，不再残留在画面里）

## v1.34.0（左右手按键绑定：cl_hand_toggle 一键两模式 —— 0 档转发引擎 switchhands（有 CD），

          1 档翻转渲染跳板字节（无 CD、无重切枪）；cl_hand_bind 一键下发 bind）

## v1.33.0（新增左右手通道 cl_bob_hand：0=原版 / 1=引擎换手链路隔离；cl_bob_hand 不持久化）

- （见源码注释）

## v1.32.0（幅度增益后台固定 10.0；cl_bob_params 后台化，固定手动档）

  · SCOPE_GAIN 由 5.0 改为 10.0，仍是源码常量：X←lat×4.0×10、Y←(vert-直流)×8.0×10。
  · cl_bob_params 从 cvar 表移除（进 RemovedCvarNames），行为固定为「1=手臂手动参数」，
    幅度直接取 cl_bobamt_vert / cl_bobamt_lat / cl_bob_lower_amt；24.3 冻结预设分支删除。
  · cvar 表 7 项：cl_bob / cl_bobcycle / cl_bobup / cl_bobamt_vert / cl_bobamt_lat /
    cl_bob_lower_amt / cl_bob_sniper。

## v1.31.0（幅度增益后台固定 5.0；镜框档位裁剪为纯开关 0/1；撤掉 amp 参数）

  · cl_bob_sniper 只保留 0=原版 / 1=legacy（覆盖式单源），原 2 档（探针/静止）整档删除，
    cvar 上限同步收到 1，旧 cfg 里的 2 会被钳成 1。
  · 1 档幅度总倍率不再走 cvar：SCOPE_GAIN=5.0 常量直接乘进覆盖值，v1.30.0 的
    cl_bob_sniper_amp 已从 cvar 表与全部代码路径移除（不再注册、不再读取）。
  · 口径：X←lat×4.0×5.0、Y←(vert-直流)×8.0×5.0。

## v1.29.0（1 档改为「覆盖式」单源联动，修掉 v1.28.0「增益恒 1 = 原值 = 原版」的无用问题）

  · 根因：v1.28.0 的 1 档是 mulss × 1.0，数学恒等，画面必然与 0 档一致 —— 用户实测「1 无用」成立。
  · 改法：cave 由「乘法」改为「替换」。0xC939E3 / 0xC939ED 处不再缩放 CS2 原生抖动，
    而是开闸时直接把这个值换成插件自己算出的 legacy 值（movss xmm,[rip+VAL]）。
  · 单源：X 站 ← _bobLatRaw，Y 站 ← _bobVertRaw，即 CalcViewModelBob 写出的同一份 vert/lat，
    与手臂通道完全同源，做到「手抖多大镜子就晃多大」，相位同源。
  · 幅度：legacy 开镜锁 0.3 / 0.5（LEGACY_SCOPED_VERT/LAT），再乘 SCOPE_LEGACY_UNIT 换算到屏幕量纲。
  · 零均值：vert = aV*(0.3+0.7sin)-lower 带正直流，纵向送镜框前先减掉 (aV*0.3-lower)，
    只留 aV*0.7*sin 这一段；否则会重演「整个框被推走」。
  · 0 档：闸门关，cave 每条指令直通原生 movss，字节级等同 CS2 原版。
  · 2 档：v1.31.0 起整档删除（cvar 上限 1），不再有静止档。

## v1.25.1（探针结果落地）：

  实测已确定唯一开镜专属路径 —— 0xC93886（"Legacy Sniper Scope" 实体更新函数内），
  其下游 0xCB1370 为 vtable 间接调用，与 0xC93886 严格 1:1，不是死代码。
  本版把 cl_bob_sniper=2 从「忽略」改为「在 0xC939E3 / 0xC939ED 两条 movss 处
  对 xmm0/xmm1 寄存器副本叠加固定静态 Δ（float）」，用于验证镜框通道是否真正生效；
  该两处是全函数唯一读取抖动 float 的地方且无写回，故不会累积、不污染游戏堆。
  0/1 档仍不动字节。
  探针保留，dump 扩到 rdi+0x00..0x24 与 rbx+0x00..0x24 各 10 个 int。

## v1.25.2（偏置可见性验证版）：

  1.25.1 的 dY=+2.0 实测「没区别」→ 该两处偏置量改为运行期 cvar：
    cl_bob_sniper_delta（0..500，默认 2）在 cl_bob_sniper=2 时叠加到 [rdi+0x10]；
    局内改值 3 秒内刷进 cave，可现场扫 2/20/50/200 找可见阈值。
  偏置 cave 内新增真实命中计数（inc dword [shared+k*4]），beat.probe 的
  bias.X/bias.Y Hz 从此是真执行频率，不再是恒 0 的占位。
----------------------------------------------------------------------------//  本插件由两个已验证的独立插件合并而成，行为逐行保留：
  · 手臂通道   ← ClBob v1.19.0        （cl_bob        0/1/2）
  · 镜框通道   ← ScopeBobSniper v1.20.3（cl_bob_sniper 0/1，无任何附加参数）
唯一的结构性改动：把所有硬编码 RVA 换成「AOB 签名扫描 → 唯一命中 → 反算 RVA」。

---------------------------------------------------------------------------
定位层（AobLocator.cs）
  运行期对 client.dll 的 .text 全段扫描，每个目标必须恰好 1 处命中；0 处或多处
  一律判定该签名不可用，对应通道直接跳过、不写任何字节。扫描成功后再做一次
  「原字节校验」（掩掉跨版本必变的位移位），通过才允许安装 hook。
  扫描失败是软失败：手臂失败不影响镜框，反之亦然。
  cl_bob_diag 改变时会打印 [loc] 定位报告：签名 / 命中数 / 解析 RVA / 与内置基准的差值。
  v1.23.5 起 cl_bob_diag 后台固定 1，启动即打一次，不再注册为可配 cvar。

---------------------------------------------------------------------------
cvar 表（一张表，两条通道共用同一份读值；ModuleName 保持对旧 cvar 完全兼容）
  cl_bob              0 原版 / 1 legacy 复刻 / 2 完全不晃（位置冻结）      [手臂]
  cl_bob_sniper       0 原版 / 1 legacy 复刻（幅度后台固定 ×5，无附加参数）  [镜框]
  cl_bobcycle         0.98   ┐
  cl_bobup            0.5    │ 只注册一份，两条通道各按自己的口径消费
  cl_bobamt_vert      0.25   │
  cl_bobamt_lat       0.40   │
  cl_bob_lower_amt    21     ┘
  cl_bob_params       v1.32.0 起后台化：固定手动参数档，不再注册 cvar、不读配置

## v1.23.5：镜框只剩一个开关 cl_bob_sniper。cl_bob_sniper_px / _invert / _lower / _bias

四项随 legacy 语义删除（legacy 里没有像素增益/翻转位/下沉开关/偏置这类附加手感），
不注册 cvar、也不留源码常量；旧 cfg 里残留的这四个键一律告警并忽略。
镜框偏移始终只由 CalcViewModelBob 写出的同一份 vert/lat 决定：手臂晃多少、镜框就晃多少。

通道隔离
  手臂 hook 段：0x4AA730 / 0x4AADAE / 0x4AAEF7 / 0x4AB04D / 0x4AADD9
  镜框 hook 段：0xCB1370
  两段相隔 ≈0x80 万字节，且安装前做区间重叠检查（Overlap 命中即拒绝安装），
  绝不会出现「两次 patch 同一地址」。

日志：<数据根>\cl_bob.log（根目录见 BOBCORE_ROOT / 服务器根目录），行首带通道前缀 [arm]/[scope]/[loc]/[cfg]
持久化：<数据根>\cl_bob_persist.cfg（格式不变，仅键名集合扩展到两条通道）
不写游戏目录：日志与 cfg 均落在服务器根目录（或 BOBCORE_ROOT 指定目录），
              绝不触碰 client.dll、也绝不触碰 plugins\ClBob 目录。
*（内容由AI生成，仅供参考）*
