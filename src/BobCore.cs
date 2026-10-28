// ============================================================================
//  v1.58.5（修 v1.58.4 引入的「更抖」：倍率估计改为窗口累积，不再逐帧 d/wallDt）
//    · v1.58.4 错在：引擎时间每 tick 才 +15.6ms、波形线程每帧 ~2ms，逐帧相除得 7~8x，
//      且只在 d>0 的帧更新 EMA → 倍率被拉到 ~7.6，相位推快 7 倍，反而更抖。
//    · 现在：0.2s 窗口内累加引擎时间与墙钟时间再相除，倍率稳定在 ~1.0（demo 倍速才偏离），
//      相位按 墙钟帧时间 x 倍率 均匀推进；120ms 引擎零推进才冻结。日志新增 engR 字段可自查。
//  ---
//  v1.58.4（修 1 档「一抖一抖」：相位推进改为 墙钟帧时间 x 引擎时间倍率(EMA) 平滑推进）
//    · 现象：幅度已合适，但晃动一顿一顿，不是连续摇摆。
//    · 根因：引擎时钟按 tick 阶跃（~64~128Hz），波形线程 ~486Hz；v1.58.0~1.58.3 每帧直接拿引擎时间差
//      推进相位 → 跳一帧、停几帧、再跳，形成阶梯推进。
//    · 改法：把引擎时间增量换算成「引擎/墙钟」倍率（0.5s EMA），每帧按墙钟帧时间 x 倍率均匀推进；
//      连续 120ms 引擎时间零推进（暂停/菜单/加载）才冻结相位；seek/回退只重置基准。
//    · 保留 v1.58.3 的 M1_AMP_SCALE=0.50 幅度降档。
//  ---
//  v1.58.3（1 档幅度降一档：vert/lat 源头乘 M1_AMP_SCALE=0.50）
//    · 现象：v1.58.2 恢复绝对波形投喂后，实战 cl_bob=1 抖太猛（枪随走动大幅前后甩）。
//    · 数据：spd=250 / cl_bobamt_vert=0.2 / cl_bob_lower_amt=15 时内部 vert≈-4.21（lower=4.5 占主导），
//      fb=vert*AX_FORWARD≈-1.68 单位，走动时随 speed 在 0↔-1.68 猛甩。
//    · 改法：vert/lat 源头统一乘 M1_AMP_SCALE（六通道 + 镜框广播同缩），仅 st==1 生效，2 档/0 档不动。
//  ---
//  v1.58.0（相位时间源改读引擎 curtime —— 修 demo / 观战下晃动不规律与相位漂移）
//    · 现象：cl_bob=1 在 demo 回放与观战里晃动节奏忽快忽慢、暂停/快进后相位与画面不同步。
//    · 根因：StepWaveArm 的 _bobTime 用 Stopwatch（墙钟）积分。demo 暂停时墙钟照走、相位继续
//      推进；demo 快进时墙钟又不跟随倍速 → 相位与引擎时间轴脱钩，越走越偏。
//    · 改法（全程进程内直读，不写外部程序、不注入）：
//        ＊ 从签名解析出的 arm.spd（0x4AADD9）往前 0x10 现算「取时间」call → 0x18046C5C0；
//        ＊ 从该函数体里现算它引用的时钟对象全局（本版 client.dll = RVA 0x182ADF0）；
//        ＊ 波形线程每帧读该对象 +0x34 的 float 时间——这正是引擎 bob 函数自己用的本帧时间
//          （0x4AADC9 把它写进 [rbp+0x50]，0x4AADD9 再用它算 viewmodel 速度），含 subtick 分数；
//        ＊ 相位增量改用引擎 dt：暂停 dt=0（相位冻结）、倍速按倍速、掉帧自动补齐；
//          时间回退 / 跳变（换图、demo seek、从头播放）只重置基准，不让相位猛跳。
//    · 兜底：任一步校验不过（call 不是 E8、找不到全局、指针为空、数值不像时间）→ 自动退回墙钟，
//      其余通道照常工作，不会因为时间源把插件拖挂。
// ============================================================================
//  v1.56.5（cl_bob=2 与游戏内 bob 指令彻底脱钩）
//    · 2 档的幅度/周期/上摆/下沉全部取插件后台常量 BG2_*：vert 0.25 / lat 0.40 /
//      lower 21 / cycle 0.98 / bobup 0.50。
//    · RefreshParamsArm 在 _state==2 时直接返回常量，不再 ReadCvar —— 游戏内改
//      cl_bobamt_vert / cl_bobamt_lat / cl_bob_lower_amt / cl_bobcycle / cl_bobup 对 2 档一律无效。
//    · 1 档行为不变（仍按游戏内 cvar）；切档时立即按新口径重取一次参数。
//    · 日志：apply 行与 beat 的 params 行新增「参数源=后台常量 / cvar」(BG2 / cvar)。
//  v1.56.4（cl_bob=2 先完全按 1 档接管）
//    · 用户口径：暂不追求 2 档额外去晃，先让 2 档严格等同 1 档的通路与数值。
//    · 实现：ApplyStateArm 入口把档位映射 2→1（_effState），此后 _inState / SH_STATE /
//      波形步进全走 1 档：快照 + 覆盖 + EXIT 站写回 base，不再触发任何 2 档归零/冻结分支。
//    · cvar 原值仍存 _state（日志显示 state=2 eff=1），便于切换对照。
//  v1.51.0（修 WA2 cave 的 jmpOff 0x31→0x32：0x31 落在 xorps xmm5 的末字节 0xE5，
//            BuildArmCave 自检拒绝构造 → wa2 站从未安装，2 档始终无效）
//  v1.50.0（修 WA2 站覆盖长度 5→6：原 5 会让 jmp 落进 movss 中间，实测必崩/失效）
//  v1.49.0（cl_bob=2 彻底不晃：补 WA2 站，清直通分支的 pose 累加）
//    · 现象：v1.45~v1.48 实测 cl_bob=2 仍原版晃动，beat 日志 wa=0/0 恒不变。
//    · 真因：v1.45 的 WA 站挂在 0x4AAF27，位于「0x4AAF04 取到对象且 0x4F4950 返回真」
//      这条分支内，运行时一帧都进不去；引擎每帧走的是 0x4AAF41 直通分支，
//      xmm3/4/5 直接吃 [rsp+0x30/34/38] 全量 pose，乘相机基向量后累加进
//      [rsp+0x40/44/48] → 相机跟着手部走路 pose 晃。
//    · 修复：新增 WA2 站 @0x4AAF4D（arm.exit+0x56，`movss xmm3,[rsp+0x30]`，5B），
//      2 档时清零 xmm3/xmm4/xmm5，两条分支汇合到 0x4AAF53 的矩阵乘后增量恒为 0。
//      原 WA 站（清 xmm6）保留，覆盖另一条分支。仅影响 viewmodel 渲染，不动弹道/视角。
//  v1.48.0（恢复 cl_bob_sniper=2 档：偏置站每帧恒写 0，镜框完全静止）
//    · 用户反馈 2 档有用，v1.31 整档删除属误删，本版按原语义补回：cvar 上限 1→2。
//    · 2 档 = 开闸 + 覆盖值恒 0（SyncBiasDelta 对 st!=1 已算 0），只静止镜框，手臂照常抖。
//    · 不恢复任何旧附加参数（_px/_invert/_lower/_bias/_delta/_gain/_amp 仍保持删除）。
//  v1.47.0（控制/观战 BOT 时镜框恒不动 = cl_bob_sniper 2 效果 → 观察目标解析修复）
//    · 现象：控制 BOT 开镜后镜框一动不动，cl_bob_sniper 10 / cl_bob 12 全无效。
//    · 证据：同场 beat.probe 日志恒为 bias gate=1 mode=replace dX=0 dY=0、arm=(0,0)，
//        说明 sniper 通道本身开着（sniper=1 生效），只是 bob 源输入恒 0。
//    · 根因（反编译 CSS API 1.0.284 元数据确认）：
//        1) ObserverServices 不在 CCSPlayerController 上，而在 **Pawn**（CBasePlayerPawn）上，
//           旧代码 p.GetType().GetProperty("ObserverServices") 永远 null；
//        2) ObserverTarget 的类型是 CHandle<CBaseEntity>，Value 取到的是实体（CCSPlayerPawn），
//           旧代码 `as CCSPlayerController` 永远 null。
//      → 观察目标解析不出来，PickLocal 退回「观战者自己」；观战者 pawn 无武器、速度 0
//        → bob 源恒 0 → 镜框静止，看上去与 cl_bob_sniper=2 一模一样。
//    · 改法：
//        1) ObserverTargetOf 改从 pawn / ObserverPawn 的 ObserverServices 取 ObserverTarget，
//           按实体 handle 回查玩家；兜底 controller.DesiredObserverTarget（CHandle<CEntityInstance>）；
//        2) PickLocal 新增 IsObserverPawn / HasWeapon 判据，排除观战者自带 observer pawn，
//           候选顺序改为「观察目标 → 有武器真人 → 有武器且最快 → bot → 最快」；
//        3) 新增 obs 诊断日志：选择目标变化时打印各玩家的 pawn 类名/是否持枪/观察目标。
// ============================================================================
//  v1.45.0（按用户指令「做」：hook 点下移到 0x4AAF27，2 档在最终变换前清 xmm6，掐断 slot→世界位移的整段 bob 累加）
//  v1.44.0（按用户指令：2 档先回归 cl_bob=1 的监管路径，再全数值调 0）
//    · v1.43.0 的速度门控实测失败：2 档仍是原版走动晃动，且手臂「一会跟一会不跟」。
//    · 「一会跟一会不跟」根因：门控判据是平滑速度 > 0.5，速度在阈值附近来回抖 →
//        base 在「实时刷新」与「冻结值」之间反复切换，手臂位置逐帧跳变。
//      → 本版把 SNAP 站 0x17 的 jne 改成 jmp（0x75 → 0xEB），门控整段变成不可达死代码，
//        base 恢复为与 1 档完全相同的「每帧刷新」，跟手不再断续。
//    · 2 档输出仍由 EXIT 站把六槽写回 shared base（= 1 档公式把全部增量取 0 的结果），
//        因此 2 档与 1 档走同一条数据通路，只是增量为 0：便于两档直接对照。
//    · 波形线程在 state==2 时把 shared 里所有增量字段（SH_FB/SH_BOB/SH_BOB_LR/SH_LOW/
//        SH_ROLL/SH_PITCH/SH_YAW）统一写 0 —— 即使 bobacc 跳板漏跳，也不会有任何增量残留。
//    · beat 日志新增 base= 六槽实测值，用于判定「数值全 0 后仍晃」是否来自 base 本身（动画层）。
// ============================================================================
//  v1.43.0（cl_bob=2 真凶定位：base 本身就是晃动来源 → 速度门控冻结 base）
//    · 现象：v1.42.1 进图不崩，但 cl_bob=2 手臂仍是原版走动晃动。
//    · 排查：反汇编 client.dll 0x4AAD00~0x4AB010 后确认整条链路——
//        0x4AAD5A call 0x54fa10 算出「viewmodel 基准 pose」写入 [rsp+0x30..0x38]，
//        SNAP 站(0x4AADAE) 把它抄进 shared base；0x4AAE4E 段再叠加数值 bob（已被 bobacc 跳过）；
//        0x4AAEE3 处 call [rax+0xa70] 的结果会二次覆盖 [rsp+0x30/34/38]，但被 exit 站(0x4AAEF7) 的 base 写回盖掉；
//        之后 0x4AAF53 起用相机基向量把 base 变换到世界坐标 —— 视角跟随走这条，与晃动无关。
//      → 结论：数值 bob 早已归零，2 档仍晃的原因是 base 每帧都在刷新，而 base 来自 0x54fa10，
//        里面带着走/跑动画驱动的骨骼位移。每帧把「含动画晃动的 base」写回 slot，等于把动画晃动搬运到最终 pose。
//    · 改法：SNAP 站加门控 —— state==2 且 速度>0.5 时不刷新 base（保留上一次站立姿态），
//        exit 站照旧把冻结的 base 写回六槽。走动中手臂完全静止；停下立刻恢复刷新，
//        蹲下/换枪/开镜的姿态更新不受影响；转视角由外部相机基向量完成，仍完整跟随。
//    · 门控布局：0x12 cmp [r11+0x18],2 / 0x17 jne 0x28 / 0x19 cmp [r11+0x80],0x32 /
//        0x24 jbe 0x28 / 0x26 jmp 0x70 / 写块 0x28~0x6F / FRZ 0x70 / CAVE_SNAP_JMP=0xAC，总长 0xB5。
//    · shared+0x80 由波形线程每 1ms 写入 (平滑速度 x100)，供门控读取。
// ============================================================================
//  v1.42.1（修复出口 cave rax 占位未补丁导致选边闪退；代表 v1.42.0 改动全保留）
//  v1.42.0（撤回左右手改造；2 档改「六槽写回本帧 base」，修 X 轴定住 / 上下视角不跟）
//    · 删除 cl_bob_hand 与 cl_hand 两个 cvar 及整条左右手通道：渲染跳板、8 个写入点 NOP、
//      手性字段写入重定向、TryGetPawn / DriveHandField / HandTapStats / Schema 偏移解析、
//      通道隔离自检里的 hand.render 项全部移除。左右手恢复引擎原生 switchhands 行为。
//      旧 cfg 里残留的 cl_bob_hand / cl_hand 进 RemovedCvarNames，加载时自动剔除。
//    · cl_bob=2 实测缺陷：v1.41.0 出口站只把 [rsp+0x50]（低值/下压槽）钉成 shared+0x90 静止值，
//      结果手臂 X 轴定住、上下晃视角不跟随。原因：六个栈 slot 是「每帧 0x54fa10 重算的基准 pose
//      + 原生 bob 累加」，单槽写常数只清一轴、且该轴失去基准。
//    · 改法：2 档出口站把 base（SNAP 站每帧刷新的 shared+0x30/0x34/0x38/0x3C/0x58/0x5C）
//      原样写回六个 slot —— bob 增量归零、基准姿势与视角跟随完整保留。出口 cave 由 0xE4 扩到 0xED。
// ============================================================================
//  v1.40.0（bob 2 档改直通，彻底去掉 FRZ 钉死；cl_hand 改「引擎反向渲染」真写手性字段）
//    · 2 档钉原地根因：v1.39.2 出口站把 [rsp+0x50]/0x54/0x58 覆盖成 FRZ 静止基线（常数），
//      这三槽是最终 viewmodel 朝向分量，被钉成常数 → 转视角手臂不跟、观感定死。
//      实测 bobacc=65/65（2 档每帧 100% 跳过累加段）已足够实现「不晃且跟随」，
//      故 2 档重新回到直通，FRZ 覆盖整段变成不可达死代码。
//    · cl_hand：不再只翻渲染标志，直接把 pawn+0x2400(m_bLeftHanded) 写成目标值，
//      让引擎本体真反向渲染；8 个写入点仍 NOP（引擎改不回去），仅在被改歪时补写（0.2s 节流）。
//  v1.39.3（修复 v1.39.2 出口站 cave 字节错位致进图闪退）
//    · 现象：v1.39.1 实测 cl_bob=2 又回到原版晃动；cl_hand 换手仍有“CD 感”。
//    · 根因一：bobacc 跳板虽已装（0x4AAE4E → 0x4AAEAD），但 pose 探针里 [rsp+0x50]
//      仍在 -1.58↔-4.65 之间颤 —— 该槽的写入还有第二条路径，只靠跳过累加段拦不住；
//      而 v1.39.1 出口站对 2 档是「直通」，等于完全不兜底。
//    · 根因二：cl_hand 的边沿驱动仍把 pawn+0x2400 写成 1（左手）并清 +0x23FC 冷却戳，
//      引擎读到手性字段变更 → 重建 viewmodel / 重播部署动画 → 观感像 CD；
//      legacy 的 cl_righthand 只翻转渲染数值，实体字段本身不动，所以零过渡。
//    · 改法一（出口站 2 档）：不再直通、也不写 0，改为把 FRZ 静止基线写进三个抖动槽
//      [rsp+0x50]/[rsp+0x54]/[rsp+0x58]（对应 LOW / ANG_Y / ANG_Z），位移基值
//      0x30/0x34/0x38 原样不动 —— 手臂不钉原地，也不叠加抖动量。
//      FRZ_OK==0（基线未就绪）时仍然直通，避免冷启动写脏值。
//    · 改法二（bobacc 加计数）：共享区 +0x88/+0x8C 累计「进入 / 真正跳过」次数，
//      beat 日志输出 bobacc=进入/跳过，用于确认 2 档是否每帧都跳过了累加段。
//    · 改法三（手性）：DriveHandField 彻底不写 pawn 任何字节，只刷渲染跳板 state，
//      左右手完全由渲染侧分支镜像（cl_hand 0=左手 走原 left 分支 / 1=右手 跳过），
//      等价 legacy 直接镜像、无过渡。
// ============================================================================
//  v1.39.0（换手不再重播“切出”动画：撤销 v1.38.x 对 pawn 手性字段的一切写入）
//    · 现象：点切 cl_hand 后画面先镜像、随即又播一遍“把枪切出来”的动画（CS2 换手感），
//      legacy 是直接镜像、不过渡。
//    · 根因：换手瞬间改写 pawn->m_bLeftHanded(+0x2400)，引擎把它当状态变更，
//      viewmodel 走一次切枪/部署过渡；v1.38.0（每 tick 钉值）与 v1.38.1（重定向写入点）
//      都会触发，只是触发点不同。
//    · 改法：cl_bob_hand=1 时把 m_bLeftHanded 恒钉成 0（右手基准）作为“静态底”，
//      6 个写入点 + 2 个时间戳一律 NOP（不再重定向下发），换手只动渲染跳板 state 字节。
//      引擎看不到字段变化 → 不重建 viewmodel、不播部署动画；镜像由渲染读点单源决定。
//    · 净效果：cl_hand 0/1 绝对赋值，下一帧完成镜像，无过渡动画、无 CD。
// ============================================================================
//  v1.37.1（左右手指令收口：删 cl_hand_toggle / cl_hand_bind（AddCommand 补全即崩，且无用）；
//            cl_bob_hand 只保留「隔离引擎换手链路」这一职责；新增 cl_hand（cvar，0=左手 / 1=右手）
//            复刻 legacy 的绝对手性切换：立即生效、无 CD、不重切枪，可 bindtoggle 绑键）
//  v1.37.0（cl_bob=2 在出口站「接管 + 全调 0」：不再直通、也不再读 base，
//            六个 slot 一律写 0 —— 位置(x/y/z)与角度(yaw/z)抖动同时为 0）
//  v1.36.0（cl_bob=2 从机制层拆除：SPD 探针把速度强制 0 让原生 comiss/jbe 整段跳过晃动块，出口站 2 档直通不再覆盖
//  v1.35.1（2 档改为「还原入口 base」：bob 增量全 0，但 base 仍随视角刷新，不会把胳膊钉在原地）
//  v1.35.0（修 cl_bob=2 仍晃：出口站由「加增量」改「绝对覆盖」——2 档在 0x4AAEF7 出口
//            直接把六个 slot（0x30/0x34/0x38/0x50/0x54/0x58）写成 FRZ 锁定值，
//            原生位置与角度抖动全部被覆盖，不再残留在画面里）
//  v1.34.0（左右手按键绑定：cl_hand_toggle 一键两模式 —— 0 档转发引擎 switchhands（有 CD），
//            1 档翻转渲染跳板字节（无 CD、无重切枪）；cl_hand_bind 一键下发 bind）
//  v1.33.0（新增左右手通道 cl_bob_hand：0=原版 / 1=引擎换手链路隔离；cl_bob_hand 不持久化）
//  v1.32.0（幅度增益后台固定 10.0；cl_bob_params 后台化，固定手动档）
//    · SCOPE_GAIN 由 5.0 改为 10.0，仍是源码常量：X←lat×4.0×10、Y←(vert-直流)×8.0×10。
//    · cl_bob_params 从 cvar 表移除（进 RemovedCvarNames），行为固定为「1=手臂手动参数」，
//      幅度直接取 cl_bobamt_vert / cl_bobamt_lat / cl_bob_lower_amt；24.3 冻结预设分支删除。
//    · cvar 表 7 项：cl_bob / cl_bobcycle / cl_bobup / cl_bobamt_vert / cl_bobamt_lat /
//      cl_bob_lower_amt / cl_bob_sniper。
// ============================================================================
//  v1.31.0（幅度增益后台固定 5.0；镜框档位裁剪为纯开关 0/1；撤掉 amp 参数）
//    · cl_bob_sniper 只保留 0=原版 / 1=legacy（覆盖式单源），原 2 档（探针/静止）整档删除，
//      cvar 上限同步收到 1，旧 cfg 里的 2 会被钳成 1。
//    · 1 档幅度总倍率不再走 cvar：SCOPE_GAIN=5.0 常量直接乘进覆盖值，v1.30.0 的
//      cl_bob_sniper_amp 已从 cvar 表与全部代码路径移除（不再注册、不再读取）。
//    · 口径：X←lat×4.0×5.0、Y←(vert-直流)×8.0×5.0。
// ============================================================================
//  v1.29.0（1 档改为「覆盖式」单源联动，修掉 v1.28.0「增益恒 1 = 原值 = 原版」的无用问题）
//    · 根因：v1.28.0 的 1 档是 mulss × 1.0，数学恒等，画面必然与 0 档一致 —— 用户实测「1 无用」成立。
//    · 改法：cave 由「乘法」改为「替换」。0xC939E3 / 0xC939ED 处不再缩放 CS2 原生抖动，
//      而是开闸时直接把这个值换成插件自己算出的 legacy 值（movss xmm,[rip+VAL]）。
//    · 单源：X 站 ← _bobLatRaw，Y 站 ← _bobVertRaw，即 CalcViewModelBob 写出的同一份 vert/lat，
//      与手臂通道完全同源，做到「手抖多大镜子就晃多大」，相位同源。
//    · 幅度：legacy 开镜锁 0.3 / 0.5（LEGACY_SCOPED_VERT/LAT），再乘 SCOPE_LEGACY_UNIT 换算到屏幕量纲。
//    · 零均值：vert = aV*(0.3+0.7sin)-lower 带正直流，纵向送镜框前先减掉 (aV*0.3-lower)，
//      只留 aV*0.7*sin 这一段；否则会重演「整个框被推走」。
//    · 0 档：闸门关，cave 每条指令直通原生 movss，字节级等同 CS2 原版。
//    · 2 档：v1.31.0 起整档删除（cvar 上限 1），不再有静止档。
// ============================================================================
﻿// ============================================================================
//  BobCore v1.25.2 — 手臂晃动 + 狙击镜黑框晃动 单插件整合版（AOB 签名定位）
// ----------------------------------------------------------------------------
//  v1.25.1（探针结果落地）：
//    实测已确定唯一开镜专属路径 —— 0xC93886（"Legacy Sniper Scope" 实体更新函数内），
//    其下游 0xCB1370 为 vtable 间接调用，与 0xC93886 严格 1:1，不是死代码。
//    本版把 cl_bob_sniper=2 从「忽略」改为「在 0xC939E3 / 0xC939ED 两条 movss 处
//    对 xmm0/xmm1 寄存器副本叠加固定静态 Δ（float）」，用于验证镜框通道是否真正生效；
//    该两处是全函数唯一读取抖动 float 的地方且无写回，故不会累积、不污染游戏堆。
//    0/1 档仍不动字节。
//    探针保留，dump 扩到 rdi+0x00..0x24 与 rbx+0x00..0x24 各 10 个 int。
//  v1.25.2（偏置可见性验证版）：
//    1.25.1 的 dY=+2.0 实测「没区别」→ 该两处偏置量改为运行期 cvar：
//      cl_bob_sniper_delta（0..500，默认 2）在 cl_bob_sniper=2 时叠加到 [rdi+0x10]；
//      局内改值 3 秒内刷进 cave，可现场扫 2/20/50/200 找可见阈值。
//    偏置 cave 内新增真实命中计数（inc dword [shared+k*4]），beat.probe 的
//    bias.X/bias.Y Hz 从此是真执行频率，不再是恒 0 的占位。
// ----------------------------------------------------------------------------//  本插件由两个已验证的独立插件合并而成，行为逐行保留：
//    · 手臂通道   ← ClBob v1.19.0        （cl_bob        0/1/2）
//    · 镜框通道   ← ScopeBobSniper v1.20.3（cl_bob_sniper 0/1，无任何附加参数）
//  唯一的结构性改动：把所有硬编码 RVA 换成「AOB 签名扫描 → 唯一命中 → 反算 RVA」。
//
//  ---------------------------------------------------------------------------
//  定位层（AobLocator.cs）
//    运行期对 client.dll 的 .text 全段扫描，每个目标必须恰好 1 处命中；0 处或多处
//    一律判定该签名不可用，对应通道直接跳过、不写任何字节。扫描成功后再做一次
//    「原字节校验」（掩掉跨版本必变的位移位），通过才允许安装 hook。
//    扫描失败是软失败：手臂失败不影响镜框，反之亦然。
//    cl_bob_diag 改变时会打印 [loc] 定位报告：签名 / 命中数 / 解析 RVA / 与内置基准的差值。
//    v1.23.5 起 cl_bob_diag 后台固定 1，启动即打一次，不再注册为可配 cvar。
//
//  ---------------------------------------------------------------------------
//  cvar 表（一张表，两条通道共用同一份读值；ModuleName 保持对旧 cvar 完全兼容）
//    cl_bob              0 原版 / 1 legacy 复刻 / 2 完全不晃（位置冻结）      [手臂]
//    cl_bob_sniper       0 原版 / 1 legacy 复刻（幅度后台固定 ×5，无附加参数）  [镜框]
//    cl_bobcycle         0.98   ┐
//    cl_bobup            0.5    │ 只注册一份，两条通道各按自己的口径消费
//    cl_bobamt_vert      0.25   │
//    cl_bobamt_lat       0.40   │
//    cl_bob_lower_amt    21     ┘
//    cl_bob_params       v1.32.0 起后台化：固定手动参数档，不再注册 cvar、不读配置
//
//  v1.23.5：镜框只剩一个开关 cl_bob_sniper。cl_bob_sniper_px / _invert / _lower / _bias
//  四项随 legacy 语义删除（legacy 里没有像素增益/翻转位/下沉开关/偏置这类附加手感），
//  不注册 cvar、也不留源码常量；旧 cfg 里残留的这四个键一律告警并忽略。
//  镜框偏移始终只由 CalcViewModelBob 写出的同一份 vert/lat 决定：手臂晃多少、镜框就晃多少。
//
//  通道隔离
//    手臂 hook 段：0x4AA730 / 0x4AADAE / 0x4AAEF7 / 0x4AB04D / 0x4AADD9
//    镜框 hook 段：0xCB1370
//    两段相隔 ≈0x80 万字节，且安装前做区间重叠检查（Overlap 命中即拒绝安装），
//    绝不会出现「两次 patch 同一地址」。
//
//  日志：<数据根>\cl_bob.log（根目录见 BOBCORE_ROOT / 服务器根目录），行首带通道前缀 [arm]/[scope]/[loc]/[cfg]
//  持久化：<数据根>\cl_bob_persist.cfg（格式不变，仅键名集合扩展到两条通道）
//  不写游戏目录：日志与 cfg 均落在服务器根目录（或 BOBCORE_ROOT 指定目录），
//                绝不触碰 client.dll、也绝不触碰 plugins\ClBob 目录。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace BobCore;

public class BobCorePlugin : BasePlugin
{
    public override string ModuleName => "BobCore";
    public override string ModuleVersion => "1.58.9";
    public override string ModuleAuthor => "tonysinke";
    public override string ModuleDescription =>
        "手臂晃动 + 狙击镜黑框晃动整合版：AOB 签名定位，cl_bob 0=原版 1=legacy 复刻；镜框档位后台固定为 legacy";

    // ================================================================ P/Invoke
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandleA(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // v1.38.0：写 pawn 手性字段前的地址合法性校验
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public int AllocationProtect;
        public UIntPtr RegionSize;
        public int State;
        public int Protect;
        public int Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

    private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint PAGE_NOACCESS = 0x01, PAGE_READONLY = 0x02, PAGE_READWRITE = 0x04, PAGE_WRITECOPY = 0x08;
    private const uint PAGE_EXECUTE_READ = 0x20, PAGE_EXECUTE_WRITECOPY = 0x80, PAGE_GUARD = 0x100;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ConVarRefInitFn(IntPtr refPtr, int unk1, short idx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegCoreFn(IntPtr refPtr, IntPtr name, ulong flags,
                                    IntPtr help, IntPtr desc, IntPtr unused);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetFloatFn(IntPtr refPtr);

    // ================================================================ cvar 表（合并）
    // 索引必须与 CvarNames 严格对应；手臂/镜框代码一律用 CI_* 常量访问，不再写裸下标。
    private const int CI_BOB = 0, CI_CYCLE = 1, CI_UP = 2, CI_VERT = 3, CI_LAT = 4, CI_LOW = 5;
    private const int CI_SNIPER = 6;
    // v1.32.0：幅度增益后台固定为 10.0（不注册 cvar、不留可调参数）
    private const float SCOPE_GAIN = 10.0f;
    // v1.29.0：镜框站点改为「覆盖式」——闸门开时用插件单源 legacy 值替换原生抖动，不再是乘法增益。
    //   X 站（0xC939E3，读 [rdi+0x0c] 横向）← _bobLatRaw × SCOPE_LEGACY_UNIT
    //   Y 站（0xC939ED，读 [rdi+0x10] 纵向）← _bobVertRaw × SCOPE_LEGACY_UNIT
    //   SCOPE_LEGACY_UNIT_* 依据：实测原生抖动 float 峰值 X(横)≈±3.1、Y(纵)≈±3.6；
    //   legacy 开镜单源 AC 幅度 lat≈0.78、vert≈0.33（cl_bobamt=2、250 速），
    //   故 X 取 4.0、Y 取 8.0，让 1 档幅度与原版同级，相位由插件单源决定。
    private const float SCOPE_LEGACY_UNIT_LAT = 4.0f;
    private const float SCOPE_LEGACY_UNIT_VERT = 8.0f;
    private const int N_CVARS = 7;
    // v1.42.0：cl_bob_hand / cl_hand 随左右手通道一并删除，现存 7 个 cvar 全部持久化。
    private const int N_PERSIST = 7;

    // v1.23.5：cl_bob_diag 后台化——不再注册 cvar，固定打开定位报告（启动时打一次）。
    private const int DIAG_FIXED = 1;

    // v1.31.0：镜框只留开关 cl_bob_sniper（0 原版 / 1 legacy ×10），探针档已删除，
    // 其余四项手感参数（px / invert / lower / bias）随 legacy 语义彻底删除，不注册、不留常量。
    private static readonly string[] RemovedCvarNames =
    { "cl_bob_sniper_px", "cl_bob_sniper_invert", "cl_bob_sniper_lower", "cl_bob_sniper_bias", "cl_bob_diag", "cl_bob_sniper_delta", "cl_bob_sniper_gain", "cl_bob_sniper_amp", "cl_bob_params", "cl_bob_hand", "cl_hand" };

    private static readonly string[] CvarNames =
    {
        "cl_bob", "cl_bobcycle", "cl_bobup", "cl_bobamt_vert", "cl_bobamt_lat", "cl_bob_lower_amt",
        "cl_bob_sniper"
    };
    private static readonly float[] CvarDefaults =
    { 0f, 0.98f, 0.5f, 0.25f, 0.40f, 21f, 1f };
    private static readonly float[] CvarMin =
    { 0f, 0.10f, 0.00f, 0.10f, 0.10f, 5f, 0f };
    private static readonly float[] CvarMax =
    { 2f, 2.00f, 1.00f, 2.00f, 2.00f, 35f, 2f };   // v1.48.0：sniper 上限恢复 2（2=镜框静止覆盖）

    private readonly IntPtr[] _refs = new IntPtr[N_CVARS];
    private readonly IntPtr[] _names = new IntPtr[N_CVARS];
    private readonly IntPtr[] _helps = new IntPtr[N_CVARS];
    private readonly ConVar[] _cvs = new ConVar[N_CVARS];

    // ================================================================ 手臂通道常量
    // v1.58.9 开源版：数据根目录不再硬编码，按 环境变量 → 服务器根目录 → 插件目录 三级回退。
    //   服务器根目录 = Server.GameDirectory（...\game\csgo）上两级，例如 D:\cs2server。
    //   需要固定位置时设环境变量 BOBCORE_ROOT=<目录>。
    private static string _dataRoot;
    private static string DataRoot
    {
        get { return _dataRoot ?? (_dataRoot = ResolveDataRoot()); }
    }
    private static string LogPath { get { return Path.Combine(DataRoot, "cl_bob.log"); } }
    private static string PersistPath { get { return Path.Combine(DataRoot, "cl_bob_persist.cfg"); } }

    private static string ResolveDataRoot()
    {
        try
        {
            string env = Environment.GetEnvironmentVariable("BOBCORE_ROOT");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        }
        catch { }
        try
        {
            string game = Server.GameDirectory;
            if (!string.IsNullOrWhiteSpace(game))
            {
                string root = Path.GetFullPath(Path.Combine(game, "..", ".."));
                if (Directory.Exists(root)) return root;
            }
        }
        catch { }
        try
        {
            string dir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }
        catch { }
        return ".";
    }

    // 共享内存布局（0x90）——与 ClBob v1.19.0 完全一致
    private const int SHARED_SIZE = 0xA0;   // v1.39.2：尾巴 0x88/0x8C 给 bobacc 命中计数
    private const int SH_FB = 0x00, SH_LOW = 0x0C, SH_BOB = 0x14, SH_STATE = 0x18;
    private const int SH_CNT_EXIT = 0x1C, SH_CNT_ENTRY = 0x20, SH_BOB_LR = 0x24;
    private const int SH_BASE_LR = 0x30, SH_BASE_FB = 0x34, SH_BASE_UD = 0x38, SH_BASE_LOW = 0x3C;
    private const int SH_CNT_SNAP = 0x40, SH_ROLL = 0x44, SH_CNT_ROLL = 0x48;
    // v1.39.2：bobacc 跳板命中回读（进入次数 / 真正跳过次数）
    private const int SH_CNT_BOBACC = 0x88, SH_CNT_BOBACC_SKIP = 0x8C;
    // v1.45.0：WA 站命中计数（0x94 = 2 档命中，0x98 = 总进入）
    private const int SH_CNT_WA2 = 0x94, SH_CNT_WA_ALL = 0x98;
    // v1.41.0：2 档单槽静止值（exit 站把 [rsp+0x50] 这个下压/低值槽写成本值）
    private const int SH_STILL_LOW = 0x90, SH_STATE_SNAP = 0x9C;
    private const int SH_PITCH = 0x4C, SH_YAW = 0x54;
    private const int SH_BASE_ANG_Y = 0x58, SH_BASE_ANG_Z = 0x5C;
    private const int SH_FRZ_LR = 0x60, SH_FRZ_FB = 0x64, SH_FRZ_UD = 0x68;
    private const int SH_FRZ_LOW = 0x6C, SH_FRZ_ANG_Y = 0x70, SH_FRZ_ANG_Z = 0x74;
    private const int SH_FRZ_OK = 0x78;
    private const int SH_VM = 0x7C;
    private const int SH_SPD_N = 0x80;
    private const int SH_SPD_SEQ = 0x84;

    private const float AX_FORWARD = 0.40f;
    private const float AX_UP = 0.10f;
    private const float AX_RIGHT = 0.20f;
    private const float K_AMP = 0.00625f;
    private const float K_AMP_NOENT = 0.00125f;
    private const float K_LOWER = 0.2f;
    private const float K_LOWER_SCALE = 0.006f;
    private const float K_SPEED_INT = 0.003125f;
    private const float CYC_K_GROUND = 0.00028f;
    private const float AIR_AMT_VERT = 0.30f;
    private const float CYC_FALLBACK_243 = 0.238f;
    private const float BOBUP_243 = 0.50f;
    private const float ANG_PITCH = -0.40f;
    private const float ANG_YAW = -0.30f;
    private const float ANG_ROLL = 0.50f;
    private const float OUT_SCALE = 1.0f;

    // v1.58.3：1 档输出幅度降档系数（用户实测“抖太猛”）。
    // 实测 spd=250、cl_bobamt_vert=0.2、cl_bob_lower_amt=15 时：
    //   aV = 250*0.00625*0.2 = 0.3125，lower = 250*(15*0.2)*0.006 = 4.5
    //   → 内部 vert = waveV - lower ≈ -4.21（lower 直流分量绝对主导）
    //   → fb = vert*AX_FORWARD = -1.68 单位，走动时随 speed 在 0↔-1.68 猛甩，观感即“抖太猛”。
    // 处理：对 vert / lat 源头统一乘该系数（六条输出通道与镜框广播同步缩），
    // 只降幅度、不改公式结构与 clamp 语义；仅 st==1 生效，2 档（FRZ 覆盖）与 0 档不受影响。
    private const float M1_AMP_SCALE = 0.50f;

    // v1.21.8：legacy scoped 硬编码幅度（legacy client.dll CalcViewModelBob 内 double 常量，
    // 0x10D2E3A8 = 0.3（vert 分支）、0x10D2E410 = 0.5（lat 分支）。scoped 时直接跳过硬编码幅度，
    // 不再读 cl_bobamt_vert/lat，同一 bob 状态既驱动 viewmodel 也驱动 CHudScope 镜框矩形。
    // v1.21.9：手臂通道（StepWaveArm）的 vert/lat 同样接入这两个常量，开镜时 hand 与 scope 两路幅度一致。
    private const float LEGACY_SCOPED_VERT = 0.30f;
    private const float LEGACY_SCOPED_LAT = 0.50f;

    private const float P24_VERT = 0.25f;
    private const float P24_LAT = 0.40f;
    private const float P24_LOWER = 21f;
    private const float P24_CYCLE = 0.98f;
    private const float P24_BOBUP = 0.50f;

    private const int FreezeSamples = 1200;

    // 旧版本遗留补丁位置（仅用于还原；写入前必须逐字节匹配，跨版本时通常不匹配 → 不写）
    private const int RVA_OLD_MID = 0x4AAE4E;
    private const int RVA_OLD_GATE = 0x4AADE4;

    private static readonly byte[] MID_ORIG = { 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30, 0xF3, 0x0F };
    private static readonly byte[] OLD_GATE_ORIG = { 0x0F, 0x86, 0xCC, 0x00, 0x00, 0x00 };
    private static readonly byte[] OLD_GATE_PATCH = { 0xE9, 0xCD, 0x00, 0x00, 0x00, 0x90 };
    private static readonly byte[] GATE_JE = { 0x0F, 0x84, 0xF8, 0x00, 0x00, 0x00 };
    private static readonly byte[] GATE_JMP = { 0xE9, 0xF9, 0x00, 0x00, 0x00, 0x90 };

    // ---------------------------------------------------------------- 手臂 cave 模板
    private static readonly byte[] CAVE_SNAP_TEMPLATE =
    {
        // v1.44.0：速度门控已废（0x17 处 jne → jmp，直接跳写块）——v1.43.0 的门控在速度阈值
        //   0.5 附近来回抖，导致 base 在「冻结」与「实时」之间反复切换，这正是「手臂一会跟一会不跟」
        //   的直接原因。本版恢复成与 1 档一致的「每帧无条件刷新 base」，SNAP 站只剩抄写 + 计数。
        //   0x19~0x26 的门控指令与 0x70 起的 FRZ 镜像段保留为不可达死代码，不再影响刷新。
        //   布局：0x12 cmp state,2 / 0x17 jmp write(0x28) / 写块 0x28~0x6F /
        //        FRZ 段 0x70 / movss 常量 0xA2 / test 0xAA / CAVE_SNAP_JMP=0xAC，总长 0xB5
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x41, 0xFF,
        0x43, 0x40, 0x41, 0x88, 0x43, 0x7C, 0x41, 0x83, 0x7B, 0x18, 0x02, 0xEB,
        0x0F, 0x41, 0x81, 0xBB, 0x80, 0x00, 0x00, 0x00, 0x32, 0x00, 0x00, 0x00,
        0x76, 0x02, 0xEB, 0x48, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x30, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x34, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x34, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x38, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x38, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x50, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x3C, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x54, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x58, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x58, 0xF3, 0x41,
        0x0F, 0x11, 0x43, 0x5C, 0x41, 0x83, 0x7B, 0x18, 0xFF, 0x75, 0x2B, 0x41,
        0x83, 0x7B, 0x78, 0x00, 0x74, 0x24, 0xF3, 0x41, 0x0F, 0x10, 0x43, 0x30,
        0xF3, 0x41, 0x0F, 0x11, 0x43, 0x30, 0xF3, 0x41, 0x0F, 0x10, 0x43, 0x34,
        0xF3, 0x41, 0x0F, 0x11, 0x43, 0x34, 0xF3, 0x41, 0x0F, 0x10, 0x43, 0x38,
        0xF3, 0x41, 0x0F, 0x11, 0x43, 0x38, 0xF3, 0x0F, 0x10, 0x35, 0x07, 0x00,
        0x00, 0x00, 0x84, 0xC0, 0xE9, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80,
        0x3F,
    };

    private static readonly byte[] CAVE_EXIT_TEMPLATE =
    {
        // v1.42.0：2 档改为「六槽全部写回本帧 base」——既不晃、又完整保留视角跟随。
        //   历史：v1.41.0 只把 [rsp+0x50] 钉成 shared+0x90 静止值，实测 X 轴定住、上下视角不跟。
        //   根因：六个栈 slot 不是纯 bob 增量，而是「每帧 0x54fa10 重算的基准 pose + 原生 bob 累加」，
        //        单槽写常数只清掉那一轴，其余轴仍带原生 bob，且被写的那一轴失去基准 → 定住。
        //   本版：base（SNAP 站 0x4AADAE 每帧刷新到 shared+0x30/0x34/0x38/0x3C/0x58/0x5C）原样写回
        //        六个 slot：bob 增量归零，基准姿势与视角跟随完整保留。
        //   布局：0 档直通(0x14 je done) / 1 档 base+增量(0x23~0x8E) / 2 档 base(0x94~0xDB)
        //        done=0xE1，CAVE_EXIT_JMP=0xED，总长 0xF2
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55,
        0x44, 0x33, 0x22, 0x11, 0x41, 0xFF,
        0x43, 0x1C, 0x41, 0x8B, 0x43, 0x18,
        0x85, 0xC0, 0x0F, 0x84, 0xC7, 0x00,
        0x00, 0x00, 0x83, 0xF8, 0x02, 0x0F,
        0x84, 0x71, 0x00, 0x00, 0x00, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x30, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x24, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x30, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x34, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x00, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x34, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x38, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x14, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x38, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x3C, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x4C, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x50, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x58, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x54, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x54, 0xF3,
        0x41, 0x0F, 0x10, 0x43, 0x5C, 0xF3,
        0x41, 0x0F, 0x58, 0x43, 0x44, 0xF3,
        0x0F, 0x11, 0x44, 0x24, 0x58, 0xE9,
        0x4D, 0x00, 0x00, 0x00, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x30, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x30, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x34, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x34, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x38, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x38, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x3C, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x50, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x58, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x54, 0xF3, 0x41,
        0x0F, 0x10, 0x43, 0x5C, 0xF3, 0x0F,
        0x11, 0x44, 0x24, 0x58, 0xE9, 0x00,
        // v1.42.1：done 段 rax 占位必须是 99 88 77 66 55 44 33 22 ——
        //   BuildArmCave 按「48 B8 99 88 77 66 55 44 33 22」精确匹配打补丁；
        //   v1.42.0 误写成 88 77 66 55 44 33 22 11（r11 那种占位），补丁不命中，
        //   rax 残留 0x1122334455667788 → call rax 直接 0xC0000005（崩溃偏移 = cave+0xEB，实测吻合）。
        0x00, 0x00, 0x00, 0x48, 0xB8, 0x99,
        0x88, 0x77, 0x66, 0x55, 0x44, 0x33,
        0x22, 0xFF, 0xD0, 0xE9, 0x00, 0x00,
        0x00, 0x00,
    };

    private static readonly byte[] CAVE_ENTRY_TEMPLATE =
    {
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x41, 0xFF,
        0x43, 0x20, 0x48, 0x8B, 0xC4, 0x55, 0x53, 0xE9, 0x00, 0x00, 0x00, 0x00,
    };

    private static readonly byte[] CAVE_ROLL_TEMPLATE =
    {
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
        0x41, 0xFF, 0x43, 0x48,
        0xFF, 0x90, 0x50, 0x08, 0x00, 0x00,
        0xE9, 0x00, 0x00, 0x00, 0x00,
    };

    private static readonly byte[] CAVE_SPD_TEMPLATE =
    {
        // v1.39.1：SPD 站退回「纯观测量」—— 只重放原 call 并计数，不再改写速度。
        //   v1.36.0 的「2 档速度置 0」会走原生 comiss/jbe 直接跳到 0x4AAEB6，
        //   顺带绕过 0x4AAEAD 处的 xmm8 恢复，且把「完全不晃」的实现压在分支条件上不可控。
        //   改为由新增的 bobacc 站点精确跳过累加段（见 CAVE_BOBACC_TEMPLATE）。
        0x48, 0xB8, 0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22,
        0xFF, 0xD0,
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
        0x41, 0xFF, 0x83, 0x84, 0x00, 0x00, 0x00,
        0xE9, 0x00, 0x00, 0x00, 0x00,
    };

    // v1.39.1：bob 累加段跳板 —— 挂 0x4AAE4E（arm.exit − 0xA9，`movss xmm0,[rsp+0x30]`）：
    //   cl_bob=2 时直接跳到 0x4AAEAD（arm.exit − 0x4A），跳过整段增量累加（含 roll），
    //   六个 slot 保持本帧原值 —— 不做任何覆盖写，位置/朝向照常跟随相机，只是不叠加晃动。
    //   这修正了 v1.35.0/v1.37.0「出口站写 0」把 slot+0x54/+0x58 一并清零、导致手臂钉在原地的错误。
    private static readonly byte[] CAVE_BOBACC_TEMPLATE =
    {
        // v1.41.0：把"实际读到的 state 值"快照到 shared+0x9C，供 beat 日志回读，
        //   一次实测即可判定 72/0 是「state 没写进去」还是「cmp/je 没生效」。
        //   同时用 push/pop rax 保护 eax，避免污染原代码后续对该寄存器的使用。
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,   // 00 movabs r11, shared
        0x41, 0xFF, 0x83, 0x88, 0x00, 0x00, 0x00,                      // 0A inc dword [r11+0x88]
        0x50,                                                          // 11 push rax
        0x41, 0x8B, 0x43, 0x18,                                        // 12 mov eax,[r11+0x18]   ← state
        0x41, 0x89, 0x83, 0x9C, 0x00, 0x00, 0x00,                      // 16 mov [r11+0x9C],eax  ← 快照
        0x83, 0xF8, 0x02,                                              // 1D cmp eax,2
        0x58,                                                          // 20 pop rax（flags 不变）
        0x74, 0x0B,                                                    // 21 je skip (0x2E)
        0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30,                            // 23 movss xmm0,[rsp+0x30]（重放原指令 6B）
        0xE9, 0x00, 0x00, 0x00, 0x00,                                  // 29 jmp back
        0x41, 0xFF, 0x83, 0x8C, 0x00, 0x00, 0x00,                      // 2E inc dword [r11+0x8C]（跳过计数）
        0xE9, 0x00, 0x00, 0x00, 0x00,                                  // 35 jmp skip（→0x4AAEAD）
    };
    private const int CAVE_BOBACC_JMP = 0x29;
    private const int CAVE_BOBACC_SKIP = 0x35;

    // v1.45.0：WA 站 —— 挂在 0x4AAF27（arm.exit + 0x30，`F3 0F 10 64 24 34` = movss xmm4,[rsp+0x34]，6B）。
    //   真因（静态核对 0x4AAE00~0x4AB053）：
    //     0x4AAE4E 的累加段对 [rsp+0x30/34/38] 与 [rsp+0x50] 加 bob 增量，但 0x4AAEDD call [vtable+0xa70]
    //     返回后 0x4AAEE6/0x4AAEF3 会把 [rsp+0x30/34/38] 整个覆盖掉（所以 exit 站写 slot 只是改「基准位置」，
    //     改不动 bob），0x50 的累加则保留。
    //     真正把晃动写进世界位移的是 0x4AAF1D~0x4AAFFC：xmm6 = (权重 − xmm0)，slot *= xmm6 后经相机基向量
    //     累加进 [rsp+0x40/44/48]，再由 0x4AB034 call 0x4C48B0（rcx=viewmodel 对象, r8=[rsp+0x40], r9=[rsp+0x50]）
    //     应用到实体，最后 0x4AB04D call [rax+0x850] 落地最终变换。
    //   本版：2 档把 xmm6 清零 → slot 的整段累加贡献为 0，[rsp+0x40/44/48] 保持原值，
    //     既不加 bob 位移，也不动相机基向量那一路 → 不晃且视角跟随。
    private static readonly byte[] CAVE_WA_TEMPLATE =
    {
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,   // 00 movabs r11, shared
        0x41, 0xFF, 0x83, 0x98, 0x00, 0x00, 0x00,                      // 0A inc dword [r11+0x98]（总命中）
        0x50,                                                          // 11 push rax
        0x41, 0x8B, 0x43, 0x18,                                        // 12 mov eax,[r11+0x18]   ← state
        0x83, 0xF8, 0x02,                                              // 16 cmp eax,2
        0x58,                                                          // 19 pop rax（flags 不变）
        0x75, 0x0A,                                                    // 1A jne replay(0x26)
        0x41, 0xFF, 0x83, 0x94, 0x00, 0x00, 0x00,                      // 1C inc dword [r11+0x94]（2 档命中）
        0x0F, 0x57, 0xF6,                                              // 23 xorps xmm6,xmm6
        0xF3, 0x0F, 0x10, 0x64, 0x24, 0x34,                            // 26 movss xmm4,[rsp+0x34]（重放原指令 6B）
        0xE9, 0x00, 0x00, 0x00, 0x00,                                  // 2C jmp back
    };
    private const int CAVE_WA_JMP = 0x2C;
    private const int WA_OFF_FROM_EXIT = 0x30;
    private const int WA_COVER = 6;

    // v1.49.0：WA2 站 —— 挂 0x4AAF4D（arm.exit + 0x56，`F3 0F 10 5C 24 30` = movss xmm3,[rsp+0x30]，5B）。
    //   实测证据（cl_bob.log beat）：wa=0/0 恒不变 —— v1.45 的 0x4AAF27 位于
    //   「0x4AAF04 取到对象且 0x4F4950 返回真」那条分支内，实际一帧都进不去，
    //   引擎每帧走的是 0x4AAF41 直通分支（xmm3/4/5 直接吃 [rsp+0x30/34/38] 全量），
    //   于是 slot 被原样乘相机基向量累加进 [rsp+0x40/44/48] → 相机仍跟着手部 pose 晃。
    //   本站在直通分支的汇合前（0x4AAF4D）落地：2 档把 xmm3/4/5 清零，
    //   两条分支最终都汇到 0x4AAF53 的矩阵乘，增量恒为 0 → 位置不再随走动摆动。
    private static readonly byte[] CAVE_WA2_TEMPLATE =
    {
        0x49, 0xBB, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,   // 00 movabs r11, shared
        0x41, 0xFF, 0x83, 0x98, 0x00, 0x00, 0x00,                      // 0A inc dword [r11+0x98]（总命中）
        0xF3, 0x0F, 0x10, 0x5C, 0x24, 0x30,                            // 11 movss xmm3,[rsp+0x30]（重放原指令 5B）
        0x50,                                                          // 16 push rax
        0x41, 0x8B, 0x43, 0x18,                                        // 17 mov eax,[r11+0x18]   ← state
        0x83, 0xF8, 0x02,                                              // 1B cmp eax,2
        0x58,                                                          // 1E pop rax（flags 不变）
        0x75, 0x10,                                                    // 20 jne done(0x32)
        0x41, 0xFF, 0x83, 0x94, 0x00, 0x00, 0x00,                      // 22 inc dword [r11+0x94]（2 档命中）
        0x0F, 0x57, 0xDB,                                              // 29 xorps xmm3,xmm3
        0x0F, 0x57, 0xE4,                                              // 2C xorps xmm4,xmm4
        0x0F, 0x57, 0xE5,                                              // 2F xorps xmm5,xmm5
        0xE9, 0x00, 0x00, 0x00, 0x00,                                  // 32 jmp back（→0x4AAF53）
    };
    // v1.51.0：jmp 实际落在 0x32（前面每条指令逐条数过，总长 0x37=55B）；
    //   之前写 0x31 落进 xorps xmm5 的最后一个字节 0xE5，BuildArmCave 自检直接拒绝构造
    //   → wa2 站从来没装上，2 档自然也从来没生效过。
    private const int CAVE_WA2_JMP = 0x32;
    private const int WA2_OFF_FROM_EXIT = 0x56;
    private const int WA2_COVER = 6;   // v1.50.0：movss xmm3,[rsp+0x30] 是 6 字节（F3 0F 10 5C 24 30），
                                       // v1.49 误写 5 会只覆盖前 5 字节、jmp 回 0x4AAF52（指令中间）→ 必崩


    private const int CAVE_IMM_OFF = 2;
    private const int CAVE_SNAP_JMP = 0xAC;
    private const int CAVE_EXIT_JMP = 237;
    private const int CAVE_ENTRY_JMP = 19;
    private const int CAVE_ROLL_JMP = 20;
    private const int CAVE_SPD_JMP = 29;

    // ================================================================ 镜框通道常量
    private const int SSH_SIZE = 0x80;
    private const int SSH_ADOPT = 0x30, SSH_BASEX = 0x34, SSH_DX = 0x38, SSH_BASEY = 0x3c, SSH_DY = 0x40;
    private const int SSH_NATX = 0x48, SSH_NATY = 0x4c, SSH_CNT = 0x20;

    // v1.24.0：修正 r9 重定向偏移（旧版把副本放在 shared+0x60 却把 r9 指到 shared+0x60，
    //   导致被调函数按 r9+0x10 读到的其实是原结构 +0x20 那一段 → 改的矩形从没被读过，
    //   这是 0/1/2 档位"完全没区别"的真正根因）。现改为：
    //   ① 原 rect（r9+0x10..0x2f，32B）整体复制到 shared+0x60；
    //   ② r9 指到 shared+0x50，于是 r9+0x10 == shared+0x60，布局与原结构完全一致；
    //   ③ rect.x / rect.y 按整型加上 Δpx（该函数把 rect 当 4 个 int 用：movups + add esi,[rbp+0x1c]）；
    //   ④ 原结构一个字节都不碰，退档即恢复。
    private static readonly byte[] CAVE_SCOPE_TEMPLATE =
    {
        0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x41, 0x0F, 0x10, 0x41, 0x10,
        0x0F, 0x11, 0x40, 0x60,
        0x41, 0x0F, 0x10, 0x49, 0x20,
        0x0F, 0x11, 0x48, 0x70,
        0x45, 0x8B, 0x51, 0x10,
        0x44, 0x89, 0x50, 0x48,
        0x45, 0x8B, 0x51, 0x14,
        0x44, 0x89, 0x50, 0x4C,
        0x45, 0x8B, 0x51, 0x10,
        0x44, 0x03, 0x50, 0x38,
        0x44, 0x89, 0x50, 0x60,
        0x45, 0x8B, 0x51, 0x14,
        0x44, 0x03, 0x50, 0x40,
        0x44, 0x89, 0x50, 0x64,
        0xFF, 0x80, 0x20, 0x00, 0x00, 0x00,
        0x4C, 0x8D, 0x48, 0x50,
        0x4C, 0x8B, 0xDC,
        0x49, 0x89, 0x5B, 0x10,
        0xE9, 0x00, 0x00, 0x00, 0x00,
    };
    private const int SCAVE_IMM_OFF = 2;
    // v1.24.0：模板 90 字节，E9 位于下标 85（rel32 写到 86..89，正好收尾）
    private const int SCAVE_JMP_OFF = 85;

    private const float LEGACY_K = 0.00625f;
    private const float LEGACY_MAXSPEED = 250f;
    private const float CLAMP_VERT_LO = -7f, CLAMP_VERT_HI = 4f;
    private const float CLAMP_LAT_LO = -8f, CLAMP_LAT_HI = 8f;

    // ================================================================ 通用状态
    private IntPtr _base = IntPtr.Zero;
    private AobLocator _loc;
    private volatile bool _inited, _abort;
    private int _tick, _initFails;
    private double _initLast = -10;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private Thread _waveThread;
    private volatile bool _waveRun;
    private int _rvaGetFloat = -1;
    private int _lastDiag = -99;

    // 手臂通道状态
    private bool _armReady;                        // _armReady=签名+字节校验都通过
    private readonly int[] _armRva = new int[5];   // 0 entry 1 snap 2 exit 3 roll 4 spd
    private IntPtr _shared = IntPtr.Zero;
    private IntPtr _zone = IntPtr.Zero;
    private IntPtr _caveExit, _caveSnap, _caveEntry, _caveRoll, _caveSpd, _caveBobAcc, _caveWa, _caveWa2;
    private bool _detourExit, _detourSnap, _detourEntry, _detourRoll, _detourSpd, _detourBobAcc, _detourWa, _detourWa2;
    private int _state = -1;
    private int _lastRawLog = -99;
    private double _lastBeatArm = -10;
    // v1.56.5：cl_bob=2 档的后台参数——与游戏内 cl_bob* 系列指令彻底脱钩，全部写死在插件里
    // v1.56.8：2 档改为「legacy 公式 + 指定幅度」——结构常量全部回填 legacy 原值，
    //          幅度三项按指定值：vert=-0.2 / lat=-0.2 / lower_amt=-10（cycle/up 未指定，取 legacy 0.98 / 0.50）。
    private const float BG2_VERT = 0f;     // 纵向幅度（负值 = 与 legacy 反相）
    private const float BG2_LAT = 0f;      // 横向幅度（负值 = 与 legacy 反相）
    private const float BG2_LOWER = -6f;     // 下沉量（负值 = 整体上抬）
    private const float BG2_CYCLE = 0f;    // 周期系数（legacy 默认）
    private const float BG2_BOBUP = 0f;    // 上摆占比（legacy 默认）

    // 结构常量：2 档全部沿用 legacy 原值，保证公式与 legacy 完全同构
    private const float BG2_K_AMP = K_AMP;               // 0.00625
    private const float BG2_K_AMP_NOENT = K_AMP_NOENT;   // 0.00125
    private const float BG2_SPD_INT = K_SPEED_INT;       // 0.003125
    private const float BG2_K_LOWER = K_LOWER;           // 0.2
    private const float BG2_K_LOWER_SCALE = K_LOWER_SCALE; // 0.006
    private const float BG2_AIR_LOWER = 0.1f;
    private const float BG2_AX_FWD = AX_FORWARD;         // 0.40
    private const float BG2_AX_UP = AX_UP;               // 0.10
    private const float BG2_AX_RIGHT = AX_RIGHT;         // 0.20
    private const float BG2_OUT_SCALE = OUT_SCALE;       // 1.0
    private const float BG2_ANG_PITCH = ANG_PITCH;       // -0.40
    private const float BG2_ANG_YAW = ANG_YAW;           // -0.30
    private const float BG2_ANG_ROLL = ANG_ROLL;         // 0.50
    private const float BG2_CLAMP_LAT = 8f;
    private const float BG2_CLAMP_VERT_MIN = -7f;
    private const float BG2_CLAMP_VERT_MAX = 4f;
    private const float BG2_SHAPE_BASE = 0.3f;
    private const float BG2_SHAPE_GAIN = 0.7f;
    private const float BG2_SCOPED_VERT = LEGACY_SCOPED_VERT; // 0.30
    private const float BG2_SCOPED_LAT = LEGACY_SCOPED_LAT;   // 0.50
    private const float BG2_MAXSPEED = LEGACY_MAXSPEED;       // 250：频率输入，按 legacy 真实速度上限       // maxspeed 通道隔离，2 档固定 0（步进已冻结，仅取 fallback 频率）

    // 当前生效的参数包：1 档 = legacy 原始值（路径行为与改造前一致）；2 档 = 上面那组 BG2 值
    private float _pKAmp = K_AMP, _pKAmpNoEnt = K_AMP_NOENT, _pSpdInt = K_SPEED_INT;
    private float _pKLower = K_LOWER, _pKLowerScale = K_LOWER_SCALE, _pAirLower = 0.1f;
    private float _pAxFwd = AX_FORWARD, _pAxUp = AX_UP, _pAxRight = AX_RIGHT, _pOutScale = OUT_SCALE;
    private float _pAngPitch = ANG_PITCH, _pAngYaw = ANG_YAW, _pAngRoll = ANG_ROLL;
    private float _pClampLat = 8f, _pClampVertMin = -7f, _pClampVertMax = 4f;
    private float _pShapeBase = 0.3f, _pShapeGain = 0.7f;
    private float _pScopedVert = LEGACY_SCOPED_VERT, _pScopedLat = LEGACY_SCOPED_LAT;

    private float _pVert = 0.25f, _pLat = 0.40f, _pLower = 21f, _pCycle = 0.98f, _pBobup = 0.5f;
    private double _bobTime;
    // v1.58.7：彻底废掉「倍率估计」，改为「引擎锚点 + 墙钟补间」的锁相推进：
    //   引擎 curtime 每次前进即把相位锚点钉过去（消除累计漂移），tick 之间用墙钟流逝补间（消除 64Hz 阶梯），
    //   相位增量恒被夹在 [0, 0.05]，绝不回退。相位推进速率理论上恒为 1.000。
    private double _wallAcc;          // 绝对墙钟（Stopwatch 秒），每帧刷新
    private double _engAnchor;        // 最近一次引擎 tick 的 curtime
    private double _wallAnchor;       // 该 tick 发生时的 _wallAcc
    private double _phaseEng;         // 当前相位时间（单调不减）
    private double _phaseAcc;         // 诊断：0.5s 窗口内相位时间推进量
    private double _phaseWallAcc;     // 诊断：0.5s 窗口内墙钟推进量
    private double _phaseRate = 1.0;  // 诊断：实测相位推进速率（应 ≈1.000）
    private double _engStill;
    private double _lastMaxUpWall;   // 引擎时间水位最后一次上升时的墙钟
    private int _engBacks;           // 采样回退次数（诊断）
    // v1.58.0：相位时间源 = 引擎时钟（client.dll 全局 +0x34，float curtime，含 subtick 分数）
    private const int ENG_CLOCK_OFF = 0x34;
    private int _engPtrRva = -1;          // 存时钟对象指针的模块全局 RVA（-1 = 未解析 → 退回墙钟）
    private IntPtr _engClock = IntPtr.Zero;
    private double _lastEngT = -1.0;
    private int _engJumps, _engStalls;
    private float _bobAmp, _bobLr, _bobRoll, _bobFb, _bobPitch, _bobYaw;
    private float _lastCycle = 0.98f, _lastMaxSpeed = 250f;
    private readonly float[] _frzAcc = new float[6];

    // v1.58.1：-bobautoload 启动开关 —— 起图等 cave 装好、等场景稳定后自动 disconnect，
    //   让 CSS 与 cave 常驻后再手动 playdemo（demo 不启 listen server，CSS 不会自己加载）。
    private bool _autoLoad;
    private bool _autoLoadDone;
    private double _autoLoadAt = -1.0;
    private readonly float[] _frz = new float[6];
    private int _frzN;
    private bool _frzValid;
    private float _spd;
    private volatile float _inSpeed, _inVert = 0.25f, _inLat = 0.40f, _inLower = 21f, _inCycle = 0.98f, _inBobup = 0.5f, _inMaxSpeed = 250f;
    private volatile bool _inAir, _inAlive, _inHasWeapon;
    private volatile int _inState;
    private volatile int _effState;     // v1.56.4：实际下发给引擎的档位（2 档暂映射为 1 档）
    private volatile int _pParams;
    private volatile bool _pSrcBg;      // v1.56.5：当前幅度参数是否来自 2 档后台常量（true=不吃游戏内 cvar）
    private static volatile string _selTag = "-";
    private int _tickRate, _tickAcc, _lastExitCnt, _lastEntryCnt, _lastSnapCnt, _lastRollCnt;
    private int _exitHz, _entryHz, _snapHz, _rollHz;
    private double _rateStamp;
    private int _nSeq;
    private double _nStamp = -10.0;
    private float _nSpd;
    private bool _nVm;
    private volatile bool _useNative;
    private double _pollStamp = -10.0;
    private double _lastTickStamp = -10.0;
    private bool _warnedParams;

    // 镜框通道状态
    private bool _scopeReady;
    private IntPtr _sShared = IntPtr.Zero;
    private IntPtr _sCave = IntPtr.Zero;
    private bool _sHooked;
    private int _sRvaRender = -1;
    private int _sState = -1;
    private byte[] _sEntryOrig8;
    private double _lastBeatScope = -10;
    private double _sBobTime;
    private float _sSpd;
    private volatile int _sInState;
    private volatile bool _sInAir;               // v1.22.0：镜框通道按 legacy k 切换（地面 0.00625 / 空中 0.00125）
    // v1.23.0：唯一 bob 状态源——legacy 里 vert/lat 只有一份，写回 player（bobstate+0x14/+0x18），
    // viewmodel 偏移与 CHudScope 镜框偏移都读它，所以手臂幅度大镜框必然同步大，不能各算一套。
    private volatile float _bobVertRaw, _bobLatRaw;
    private volatile float _bobVertDc;      // v1.29.0：vert 的直流分量（aV*0.3 - lower），镜框侧减掉以保证零均值
    private volatile bool _bobRawOk;
    private volatile float _sInSpeed, _sInDir;
    private volatile float _sVert = 0.25f, _sLat = 0.40f, _sCycle = 0.98f, _sBobup = 0.5f;
    private volatile bool _sScoped;              // v1.23.2：本地 viewmodel 的 m_bShouldIgnoreOffsetAndAccuracy（legacy 0.3/0.5 的唯一判据位）
    private bool _sScopedProbeOk;
    private bool _sVmMissLogged;
    private int _sWaveHz, _sLastCnt;
    private double _sHzStamp;

    // —— v1.25.0 探针（只计数，不改游戏行为）——
    private const bool PROBE_MODE = true;
    private const int PSH_SIZE = 0x100;

    // v1.25.1 探针共享区布局（cave 侧按绝对偏移访问，改这里必须同步 cave 生成的立即数）
    private const int PSH_CNT = 0x00;      // 0x00..0x13 五站点计数
    private const int PSH_RDI = 0x20;      // 0x20..0x44 rdi+0x00..0x24 十个 int
    private const int PSH_RBX = 0x50;      // 0x50..0x74 rbx+0x00..0x24 十个 int

    // v1.25.1 偏置 cave 的私有数据区（每个 bias 站点各自的 cave 内存里）
    private const int CAVE_BIAS_FLAG = 0x40;   // 1 字节：1 = 生效（v1.25.2 代码段加命中计数，数据区后移到 0x40）
    private const int CAVE_BIAS_VAL = 0x44;    // float：叠加量

    // 2 档默认增益（v1.26.0：mulss 乘性缩放，保持零均值抖动，不产生整体位移）
    private const float SCOPE_PROBE_DELTA_X = 1.0f;   // v1.26.0：增益默认 1.0 = 原样（恒等）
    private const float SCOPE_PROBE_DELTA_Y = 1.0f;

    private IntPtr _pShared = IntPtr.Zero;
    private IntPtr[] _pCave;
    private bool _probesReady;
    private readonly int[] _pHits = new int[8];
    private readonly int[] _pLastCnt = new int[8];

    // 持久化
    private readonly float[] _persistVal = new float[N_CVARS];
    private bool _persistDirty;
    private double _persistStamp;

    // ================================================================ 日志
    private static void Log(string msg)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg;
        try { File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        try { Console.WriteLine("[BobCore] " + msg); } catch { }
    }

    private static void LogArm(string msg) { Log("[arm] " + msg); }
    private static void LogScope(string msg) { Log("[scope] " + msg); }
    private static void LogLoc(string msg) { Log("[loc] " + msg); }
    private static void LogCfg(string msg) { Log("[cfg] " + msg); }

    // ================================================================ 生命周期
    public override void Load(bool hotReload)
    {
        try { File.WriteAllText(LogPath, ""); } catch { }
        Log("==== BobCore v1.58.9 加载 (hotReload=" + hotReload + ") ====");
        // v1.58.1：-bobautoload 启动开关（自定义参数，引擎忽略），用于「起图装 cave → 自动 disconnect」流程
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-bobautoload", StringComparison.OrdinalIgnoreCase))
                {
                    _autoLoad = true;
                    Log("[autoload] 检测到 -bobautoload：cave 装好后将自动 disconnect");
                }
            }
        }
        catch { }
        Log("整合：手臂通道 ← ClBob v1.19.0 / 镜框通道 ← ScopeBobSniper v1.20.3；定位层为 AOB 签名扫描（v1.51.0：WA2 cave jmpOff 修正 0x32）");
        RegisterListener<Listeners.OnTick>(OnTick);

        // v1.42.0：左右手通道整体撤除 —— 不注册 cl_bob_hand / cl_hand，也不注册任何 ConCommand；
        //   手性交还引擎原生 switchhands / cl_prefer_lefthanded。

        // 仿 ScopeBobSniper v1.20.2 的做法：Load 阶段就把 cvar 注册掉，
        // 否则服务器没跑地图（主菜单/暂停）时 OnTick 不触发 → 控制台查不到 cl_bob。
        TryEarlyRegisterCvars();
    }

    public override void Unload(bool hotReload)
    {
        Log("==== BobCore 卸载：恢复原生字节 ====");
        try { if (_persistDirty) SavePersist(); } catch { }
        _waveRun = false;

        if (_armReady) UninstallArm();
        if (_probesReady) UninstallProbes();   // v1.25.0：必须先还原探针，否则热重载后 E9 会跳进已释放的 cave
        if (_scopeReady) UninstallScope();

        if (_zone != IntPtr.Zero) { try { VirtualFree(_zone, UIntPtr.Zero, MEM_RELEASE); } catch { } _zone = IntPtr.Zero; }
        if (_shared != IntPtr.Zero) { try { Marshal.FreeHGlobal(_shared); } catch { } _shared = IntPtr.Zero; }
        if (_sCave != IntPtr.Zero) { try { VirtualFree(_sCave, UIntPtr.Zero, MEM_RELEASE); } catch { } _sCave = IntPtr.Zero; }
        if (_sShared != IntPtr.Zero) { try { Marshal.FreeHGlobal(_sShared); } catch { } _sShared = IntPtr.Zero; }
        if (_pShared != IntPtr.Zero) { try { Marshal.FreeHGlobal(_pShared); } catch { } _pShared = IntPtr.Zero; }
    }

    private void TryEarlyRegisterCvars()
    {
        try
        {
            IntPtr b = GetModuleHandleA("client.dll");
            if (b == IntPtr.Zero)
            {
                LogCfg("Load 阶段 client.dll 尚未就绪，cvar 注册推迟到 Init");
                return;
            }
            _base = b;
            EnsureLocator();
            if (_loc == null || !_loc.TextMapped)
            {
                LogCfg("Load 阶段 .text 不可用，cvar 注册推迟到 Init");
                return;
            }
            _loc.ScanAll();
            TryRegisterCvars("Load");
            RefreshCvarHandles();
        }
        catch (Exception ex)
        {
            LogCfg("Load 阶段 cvar 注册异常（不致命，Init 将重试）：" + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void EnsureLocator()
    {
        if (_loc != null) return;
        _loc = new AobLocator(_base);
        _loc.MapText();
    }

    // ================================================================ 主循环
    private void OnTick()
    {
        _lastTickStamp = _sw.Elapsed.TotalSeconds;
        if (_abort) return;

        if (!_inited)
        {
            if (++_tick < 32) return;
            double nowSec = _sw.Elapsed.TotalSeconds;
            if (nowSec - _initLast < 1.0) return;
            _initLast = nowSec;
            try
            {
                Init();
            }
            catch (Exception ex)
            {
                _initFails++;
                Log("Init 异常 #" + _initFails + ": " + ex.GetType().Name + ": " + ex.Message);
                if (_initFails >= 5)
                {
                    // v1.23.1：不再因为单一通道异常把整个插件打死；已就绪通道继续工作
                    if (_armReady || _scopeReady)
                    {
                        Log("Init 连续失败 5 次，停止重试注入；已就绪通道继续工作（arm="
                            + (_armReady ? "ok" : "skip") + " scope=" + (_scopeReady ? "ok" : "skip") + "）");
                        try { StartWaveThread(); } catch (Exception wex) { Log("波形线程启动异常：" + wex.Message); }
                        _inited = true;
                    }
                    else
                    {
                        Log("Init 连续失败 5 次，放弃注入（保留原生行为）");
                        _abort = true;
                    }
                }
            }
            return;
        }

        _tickAcc++;

        // v1.58.1：-bobautoload —— 起图后等 6 秒（cave 注入 + 场景初始化稳定）再断开，
        //   demo 不启 listen server 不会加载 CSS，靠这一步把插件与 cave 常驻在进程里。
        if (_autoLoad && !_autoLoadDone && _inited)
        {
            double nowA = _sw.Elapsed.TotalSeconds;
            if (_autoLoadAt < 0)
            {
                _autoLoadAt = nowA + 6.0;
                Log("[autoload] 计时开始：6 秒后自动 disconnect（arm=" + (_armReady ? "ok" : "skip")
                    + " scope=" + (_scopeReady ? "ok" : "skip") + "）");
            }
            else if (nowA >= _autoLoadAt)
            {
                _autoLoadDone = true;
                try
                {
                    Server.ExecuteCommand("disconnect");
                    Log("[autoload] 已下发 disconnect —— 插件与 cave 常驻保留，可直接 playdemo");
                }
                catch (Exception aex)
                {
                    Log("[autoload] disconnect 下发失败：" + aex.GetType().Name + ": " + aex.Message);
                }
            }
        }

        ProbeVmMotion();

        // ---- 手臂通道状态
        if (_armReady)
        {
            float v = ReadCvar(CI_BOB);
            int want = (int)Math.Round(v);
            if (want < 0) want = 0; else if (want > 2) want = 2;

            int raw = (int)Math.Round(v * 100);
            if (raw != _lastRawLog)
            {
                _lastRawLog = raw;
                LogArm("读到 cl_bob = " + v.ToString("0.###") + "（通道=" + (_cvs[CI_BOB] != null ? "CSS-ConVar" : "ref8-GetFloat") + "）");
            }

            if (want != _state)
            {
                int prev = _state;
                _state = want;
                ApplyStateArm(want);
                LogArm("切换 " + prev + " -> " + want + (want == 2 ? "（按 1 档接管）" : "") + "：" + StateNameArm(_effState));
            }
        }

        // ---- 镜框通道状态
        if (_scopeReady)
        {
            int sw = (int)Math.Round(ReadCvar(CI_SNIPER));   // v1.48.0：0 原版 / 1 legacy / 2 静止
            if (sw < 0) sw = 0; else if (sw > 2) sw = 2;
            if (sw != _sState)
            {
                int prev = _sState;
                _sState = sw;
                ApplyStateScope(sw);
                LogScope("切换 " + prev + " -> " + sw + "：" + StateNameScope(sw));
            }
        }

        if ((++_tick & 3) == 0)
        {
            if (_armReady) RefreshParamsArm();
            if (_scopeReady) RefreshParamsScope();
            PersistCheck();
            DiagCheck();
        }

        if (_armReady) { SampleInputsArm(); PoseLog(); }
        if (_scopeReady) SampleInputsScope();

        BeatArm();
        BeatScope();
    }

    // ================================================================ 初始化（定位层 + 双通道）
    private void Init()
    {
        _base = GetModuleHandleA("client.dll");
        if (_base == IntPtr.Zero) { Log("找不到 client.dll，中止"); _abort = true; return; }
        Log("client.dll base = 0x" + _base.ToInt64().ToString("X"));

        // ---------- 1) AOB 定位层
        EnsureLocator();
        if (!_loc.TextMapped)
        {
            LogLoc("无法解析 client.dll 的 .text 节表 → 定位层不可用，两条通道全部跳过（保持原版行为）");
        }
        else
        {
            _loc.ScanAll();
            LogLoc(".text 已映射：RVA 0x" + _loc.TextRva.ToString("X") + " 长度 0x" + _loc.TextLen.ToString("X") + "，开始签名扫描");
            DumpLocTable("init");
        }

        // ---------- 2) cvar 注册（依赖 cvar 三处签名）
        TryRegisterCvars("Init");
        RefreshCvarHandles();
        LoadPersisted();

        // ---------- 3) 手臂通道
        TryInitArm();

        // ---------- 4) 镜框通道
        TryInitScope();

        // ---------- 5) 通道重叠自检 + 后台线程
        CheckChannelOverlap();

        if (_armReady || _scopeReady)
        {
            StartWaveThread();
        }
        else
        {
            Log("两条通道均未就绪：插件只提供 cvar 与诊断，不写任何代码字节");
        }

        _lastBeatArm = _sw.Elapsed.TotalSeconds;
        _rateStamp = _lastBeatArm;
        _lastDiag = -99;   // v1.23.5：cl_bob_diag 后台固定为 1，首次 DiagCheck 必打一次定位报告

        Log("初始化完成：arm=" + (_armReady ? "ok" : "skip") + " scope=" + (_scopeReady ? "ok" : "skip")
            + " cl_bob=" + ReadCvar(CI_BOB).ToString("0.###") + " sniper=" + ReadCvar(CI_SNIPER).ToString("0.###"));
        _inited = true;
    }

    private void TryRegisterCvars(string stage)
    {
        int rInit = _loc != null ? _loc.Rva("cvar.refinit") : -1;
        int rReg = _loc != null ? _loc.Rva("cvar.regcore") : -1;
        int rGet = _loc != null ? _loc.Rva("cvar.getfloat") : -1;

        if (rInit <= 0 || rReg <= 0 || rGet <= 0)
        {
            LogCfg(stage + " 阶段：cvar 注册链签名未命中（refinit=" + rInit + " regcore=" + rReg + " getfloat=" + rGet
                + "）→ 跳过注册，只能用 ConVar.Find 读取已存在的 cvar；两条通道仍按原版行为运行");
            _rvaGetFloat = rGet;
            return;
        }

        _rvaGetFloat = rGet;
        var initFn = Marshal.GetDelegateForFunctionPointer<ConVarRefInitFn>(_base + rInit);
        var regFn = Marshal.GetDelegateForFunctionPointer<RegCoreFn>(_base + rReg);

        int added = 0, reused = 0;
        for (int i = 0; i < N_CVARS; i++)
        {
            if (_refs[i] != IntPtr.Zero) { reused++; continue; }

            bool exists = false;
            try { exists = ConVar.Find(CvarNames[i]) != null; } catch { }
            if (exists) { reused++; continue; }

            _refs[i] = Marshal.AllocHGlobal(0x10);
            for (int k = 0; k < 0x10; k++) Marshal.WriteByte(_refs[i], k, 0);
            _names[i] = Marshal.StringToHGlobalAnsi(CvarNames[i]);
            _helps[i] = Marshal.StringToHGlobalAnsi("BobCore (arm+scope bob): " + CvarNames[i]);
            initFn(_refs[i], -1, 7);

            IntPtr desc = Marshal.AllocHGlobal(0x48);
            for (int k = 0; k < 0x48; k++) Marshal.WriteByte(desc, k, 0);
            WriteF(desc, 0x07, CvarDefaults[i]);
            Marshal.WriteByte(desc, 0x04, 1);
            WriteF(desc, 0x17, CvarMin[i]);
            Marshal.WriteByte(desc, 0x05, 1);
            WriteF(desc, 0x27, CvarMax[i]);
            Marshal.WriteByte(desc, 0x06, 1);
            Marshal.WriteInt16(desc, 0x40, 7);

            regFn(_refs[i], _names[i], 0x88UL & ~0x400UL, _helps[i], desc, IntPtr.Zero);

            float got = -1f;
            try
            {
                if (Marshal.ReadInt64(_refs[i], 8) != 0)
                    got = Marshal.GetDelegateForFunctionPointer<GetFloatFn>(_base + rGet)(_refs[i]);
            }
            catch { }
            added++;
            LogCfg("注册 " + CvarNames[i] + " 默认=" + CvarDefaults[i].ToString("0.###")
                + " 区间=[" + CvarMin[i].ToString("0.###") + "," + CvarMax[i].ToString("0.###") + "]"
                + " 读回=" + got.ToString("0.###") + (got < 0f ? "  ←注册失败(ref+8=0)" : ""));
        }
        LogCfg(stage + " 阶段 cvar 表就绪：新注册 " + added + " 项，复用已存在 " + reused + " 项（共 " + N_CVARS + " 项）");
    }

    private void RefreshCvarHandles()
    {
        for (int i = 0; i < N_CVARS; i++)
        {
            try { _cvs[i] = ConVar.Find(CvarNames[i]); } catch { }
        }
    }

    // ================================================================ 手臂通道初始化
    private void TryInitArm()
    {
        // v1.23.1 幂等守卫：Init 重试时不得因自己的 E9 覆盖而把已就绪的通道判死
        if (_armReady)
        {
            LogArm("通道 arm 已就绪，跳过重复安装");
            return;
        }
        if (_loc == null || !_loc.TextMapped)
        {
            LogArm("通道 arm 跳过：定位层不可用（.text 未映射）");
            return;
        }

        string[] keys = { "arm.entry", "arm.snap", "arm.exit", "arm.roll", "arm.spd" };
        List<string> bad = new List<string>();
        foreach (string k in keys)
        {
            SigResult s = _loc.Get(k);
            if (s == null || !s.Unique) bad.Add(k + "(" + (s == null ? "缺失" : s.Reason) + ")");
        }
        if (bad.Count > 0)
        {
            LogArm("通道 arm 签名未命中，已跳过，其余通道继续：" + string.Join("; ", bad.ToArray()));
            return;
        }

        for (int i = 0; i < 5; i++) _armRva[i] = _loc.Rva(keys[i]);
        foreach (int r in _armRva)
        {
            if (!_loc.InText(r))
            {
                LogArm("通道 arm 解析出的 RVA 0x" + r.ToString("X") + " 不在 .text 内 → 跳过");
                return;
            }
        }
        LogArm("签名解析 RVA：entry=0x" + _armRva[0].ToString("X") + " snap=0x" + _armRva[1].ToString("X")
            + " exit=0x" + _armRva[2].ToString("X") + " roll=0x" + _armRva[3].ToString("X")
            + " spd=0x" + _armRva[4].ToString("X"));

        // 原字节校验（按签名掩码只比对固定位：跨版本的 rel32/rip 位移不参与比对）
        int[] cover = { 5, 10, 5, 6, 5 };
        for (int i = 0; i < 5; i++)
        {
            if (!VerifyHookBytes(keys[i], _armRva[i], cover[i]))
            {
                LogArm("通道 arm 跳过：0x" + _armRva[i].ToString("X") + " 原字节校验不通过（" + keys[i] + "）");
                return;
            }
        }
        LogArm("5 个点原字节校验全部通过");

        // v1.58.0：解析引擎时钟（相位时间源）。失败只影响时间源，不影响 detour 安装。
        ResolveEngineClock();

        // 跨版本提醒：call 目标由签名扫到的位置现算，不再写死
        int exitCall = CallTargetRva(_armRva[2]);
        int spdCall = CallTargetRva(_armRva[4]);
        LogArm("cave 内需重放的 call 目标：exit→0x" + exitCall.ToString("X") + "（基准 0x4D1620，差值 "
            + AobLocator.Signed(exitCall - 0x4D1620) + "）  spd→0x" + spdCall.ToString("X") + "（基准 0x12C500，差值 "
            + AobLocator.Signed(spdCall - 0x12C500) + "）");

        // 遗留补丁清理（写入前逐字节匹配，跨版本通常直接跳过）
        RestoreLegacyPatches();

        _shared = Marshal.AllocHGlobal(SHARED_SIZE);
        for (int i = 0; i < SHARED_SIZE; i++) Marshal.WriteByte(_shared, i, 0);

        if (!InstallArmDetours(exitCall, spdCall))
        {
            LogArm("通道 arm 跳过：detour 安装失败");
            return;
        }

        _armReady = true;

        float v = ReadCvar(CI_BOB);
        _state = (int)Math.Round(v);
        if (_state < 0) _state = 0; else if (_state > 2) _state = 2;
        RefreshParamsArm();
        ApplyStateArm(_state);

        _lastExitCnt = _lastEntryCnt = _lastSnapCnt = _lastRollCnt = 0;
        _tickAcc = 0;
        _lastRawLog = (int)Math.Round(v * 100);
        LogArm("通道 arm 就绪，cl_bob=" + v.ToString("0.###") + " 状态=" + StateNameArm(_effState) + (v >= 1.5f ? "（2 档按 1 档接管）" : ""));
    }

    /// <summary>按签名掩码比对 hook 点的当前字节（只比固定位）。</summary>
    private bool VerifyHookBytes(string sigName, int rva, int len)
    {
        SigResult s = _loc.Get(sigName);
        if (s == null) return false;
        byte[] cur = ReadBytes(rva, len);
        if (cur == null || cur.Length < len) return false;
        for (int i = 0; i < len; i++)
        {
            if (s.Mask[i] == 0) continue;
            if (cur[i] != s.Pat[i])
            {
                LogArm("字节校验失败 @" + sigName + "+" + i + "：实际 " + cur[i].ToString("X2")
                    + " 期望 " + s.Pat[i].ToString("X2") + "（当前 " + Hex(cur) + "）");
                return false;
            }
        }
        return true;
    }

    /// <summary>读出 call rel32 的目标 RVA（用于 cave 内重放原调用）。</summary>
    private int CallTargetRva(int rvaCallSite)
    {
        byte[] b = ReadBytes(rvaCallSite + 1, 4);
        if (b == null || b.Length < 4) return -1;
        int rel = BitConverter.ToInt32(b, 0);
        return rvaCallSite + 5 + rel;
    }

    private bool InstallArmDetours(int exitCallRva, int spdCallRva)
    {
        IntPtr zone = AllocNear(_base, 0x2000);
        if (zone == IntPtr.Zero) { LogArm("cave 分配失败"); return false; }
        _zone = zone;
        _caveExit = zone;
        _caveSnap = zone + 0x400;
        _caveEntry = zone + 0x800;
        _caveRoll = zone + 0xC00;
        _caveSpd = zone + 0x1000;
        _caveBobAcc = zone + 0x1400;
        _caveWa = zone + 0x1800;
        _caveWa2 = zone + 0x1C00;

        // v1.39.1：bobacc 站点 = exit 签名位置 − 0xA9（0x4AAE4E，`F3 0F 10 44 24 30` = movss xmm0,[rsp+0x30]，6B）
        //   跳过目标 = exit − 0x4A（0x4AAEAD，xmm8 恢复处，含该恢复所以不会漏寄存器）
        int bobAccRva = _armRva[2] - 0xA9;
        int bobAccSkipRva = _armRva[2] - 0x4A;
        const int bobAccCover = 6;
        byte[] bobAccOrig = { 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30 };
        byte[] bobAccCur = ReadBytes(bobAccRva, bobAccCover);
        bool bobAccOk = bobAccCur != null && bobAccCur.Length == bobAccCover;
        if (bobAccOk)
        {
            for (int i = 0; i < bobAccCover; i++) if (bobAccCur[i] != bobAccOrig[i]) bobAccOk = false;
        }
        if (!bobAccOk)
        {
            LogArm("bobacc 原字节校验不通过 @0x" + bobAccRva.ToString("X") + "（当前 "
                + (bobAccCur == null ? "读取失败" : Hex(bobAccCur)) + "，期望 F30F10442430）→ 2 档将退回不跳过模式");
        }

        byte[] exCode = BuildArmCave(CAVE_EXIT_TEMPLATE, _caveExit.ToInt64(), _shared.ToInt64(),
                                     _base.ToInt64() + _armRva[2] + 5, CAVE_EXIT_JMP,
                                     _base.ToInt64() + exitCallRva);
        byte[] snCode = BuildArmCave(CAVE_SNAP_TEMPLATE, _caveSnap.ToInt64(), _shared.ToInt64(),
                                     _base.ToInt64() + _armRva[1] + 10, CAVE_SNAP_JMP);
        byte[] enCode = BuildArmCave(CAVE_ENTRY_TEMPLATE, _caveEntry.ToInt64(), _shared.ToInt64(),
                                     _base.ToInt64() + _armRva[0] + 5, CAVE_ENTRY_JMP);
        byte[] roCode = BuildArmCave(CAVE_ROLL_TEMPLATE, _caveRoll.ToInt64(), _shared.ToInt64(),
                                     _base.ToInt64() + _armRva[3] + 6, CAVE_ROLL_JMP);
        byte[] spCode = BuildArmCave(CAVE_SPD_TEMPLATE, _caveSpd.ToInt64(), _shared.ToInt64(),
                                     _base.ToInt64() + _armRva[4] + 5, CAVE_SPD_JMP,
                                     _base.ToInt64() + spdCallRva);

        // v1.45.0：WA 站（0x4AAF27）—— 2 档清 xmm6，掐掉 slot→[rsp+0x40/44/48] 的整段 bob 累加
        int waRva = _armRva[2] + WA_OFF_FROM_EXIT;
        byte[] waOrig = { 0xF3, 0x0F, 0x10, 0x64, 0x24, 0x34 };
        byte[] waCur = ReadBytes(waRva, WA_COVER);
        bool waOk = waCur != null && waCur.Length == WA_COVER;
        if (waOk)
        {
            for (int i = 0; i < WA_COVER; i++) if (waCur[i] != waOrig[i]) waOk = false;
        }
        if (!waOk)
        {
            LogArm("wa 原字节校验不通过 @0x" + waRva.ToString("X") + "（当前 "
                + (waCur == null ? "读取失败" : Hex(waCur)) + "，期望 F30F10642434）→ 2 档最终变换接管不挂载");
        }
        byte[] waCode = null;
        if (waOk)
        {
            waCode = BuildArmCave(CAVE_WA_TEMPLATE, _caveWa.ToInt64(), _shared.ToInt64(),
                                  _base.ToInt64() + waRva + WA_COVER, CAVE_WA_JMP);
            if (waCode == null)
            {
                LogArm("wa cave 构造失败（rel32 不可达）→ 2 档最终变换接管不挂载");
                waOk = false;
            }
        }
        // v1.49.0：WA2 站（0x4AAF4D，直通分支汇合前）—— 2 档清 xmm3/4/5，掐掉 slot→[rsp+0x40/44/48] 的全量累加
        int wa2Rva = _armRva[2] + WA2_OFF_FROM_EXIT;
        byte[] wa2Orig = { 0xF3, 0x0F, 0x10, 0x5C, 0x24, 0x30 };
        byte[] wa2Cur = ReadBytes(wa2Rva, WA2_COVER);
        bool wa2Ok = wa2Cur != null && wa2Cur.Length == WA2_COVER;
        if (wa2Ok)
        {
            for (int i = 0; i < WA2_COVER; i++) if (wa2Cur[i] != wa2Orig[i]) wa2Ok = false;
        }
        if (!wa2Ok)
        {
            LogArm("wa2 原字节校验不通过 @0x" + wa2Rva.ToString("X") + "（当前 "
                + (wa2Cur == null ? "读取失败" : Hex(wa2Cur)) + "，期望 F30F105C2430）→ 2 档直通分支不挂载");
        }
        byte[] wa2Code = null;
        if (wa2Ok)
        {
            wa2Code = BuildArmCave(CAVE_WA2_TEMPLATE, _caveWa2.ToInt64(), _shared.ToInt64(),
                                   _base.ToInt64() + wa2Rva + WA2_COVER, CAVE_WA2_JMP);
            if (wa2Code == null)
            {
                LogArm("wa2 cave 构造失败（rel32 不可达）→ 2 档直通分支不挂载");
                wa2Ok = false;
            }
        }
        if (exCode == null || snCode == null || enCode == null || roCode == null || spCode == null)
        {
            LogArm("cave 构造失败（rel32 不可达）");
            return false;
        }
        // v1.39.3：出口站模板必须严格以 jmp 结尾（jmpOff + 5 == 总长）。
        //   模板里一旦多/少一个字节，done、movabs、call rax 会整体错位，
        //   jmpOff 回填就会写进 call rax 的字节里 → 进图首帧即 0xC0000005。宁可不挂也不崩。
        if (exCode.Length != CAVE_EXIT_JMP + 5)
        {
            LogArm("exit 模板自检失败：len=" + exCode.Length + "B，期望 " + (CAVE_EXIT_JMP + 5)
                + "B（jmp 必须落在模板末尾）→ 中止挂载");
            return false;
        }

        // v1.39.1：bobacc 跳板（仅 2 档生效）
        byte[] bbCode = null;
        if (bobAccOk)
        {
            bbCode = BuildArmCave(CAVE_BOBACC_TEMPLATE, _caveBobAcc.ToInt64(), _shared.ToInt64(),
                                  _base.ToInt64() + bobAccRva + bobAccCover, CAVE_BOBACC_JMP);
            if (bbCode == null)
            {
                LogArm("bobacc cave 构造失败（rel32 不可达）→ 2 档将退回不跳过模式");
                bobAccOk = false;
            }
            else
            {
                long skipRel = (_base.ToInt64() + bobAccSkipRva) - (_caveBobAcc.ToInt64() + CAVE_BOBACC_SKIP + 5);
                if (skipRel < int.MinValue || skipRel > int.MaxValue)
                {
                    LogArm("bobacc skip 目标 rel32 不可达 → 2 档将退回不跳过模式");
                    bobAccOk = false;
                    bbCode = null;
                }
                else
                {
                    BitConverter.GetBytes((int)skipRel).CopyTo(bbCode, CAVE_BOBACC_SKIP + 1);
                }
            }
        }

        Marshal.Copy(exCode, 0, _caveExit, exCode.Length);
        Marshal.Copy(snCode, 0, _caveSnap, snCode.Length);
        Marshal.Copy(enCode, 0, _caveEntry, enCode.Length);
        Marshal.Copy(roCode, 0, _caveRoll, roCode.Length);
        Marshal.Copy(spCode, 0, _caveSpd, spCode.Length);
        if (bbCode != null) Marshal.Copy(bbCode, 0, _caveBobAcc, bbCode.Length);
        if (waCode != null) Marshal.Copy(waCode, 0, _caveWa, waCode.Length);
        if (wa2Code != null) Marshal.Copy(wa2Code, 0, _caveWa2, wa2Code.Length);
        FlushInstructionCache(GetCurrentProcess(), zone, (UIntPtr)0x2000);
        LogArm("cave exit@0x" + _caveExit.ToInt64().ToString("X") + "(" + exCode.Length + "B) snap@0x"
            + _caveSnap.ToInt64().ToString("X") + "(" + snCode.Length + "B) entry@0x" + _caveEntry.ToInt64().ToString("X")
            + "(" + enCode.Length + "B) roll@0x" + _caveRoll.ToInt64().ToString("X") + "(" + roCode.Length
            + "B) spd@0x" + _caveSpd.ToInt64().ToString("X") + "(" + spCode.Length + "B)");

        // 顺序：先装更深处的 detour，再装出口，避免中间态
        if (bbCode != null)
        {
            if (!InstallJump(_caveBobAcc, bobAccRva, bobAccCover)) { RollbackArm(); return false; }
            _detourBobAcc = true;
        }
        if (!InstallJump(_caveSnap, _armRva[1], 10)) { RollbackArm(); return false; }
        _detourSnap = true;
        if (!InstallJump(_caveExit, _armRva[2], 5)) { RollbackArm(); return false; }
        _detourExit = true;
        if (!InstallJump(_caveEntry, _armRva[0], 5)) { RollbackArm(); return false; }
        _detourEntry = true;
        if (!InstallJump(_caveRoll, _armRva[3], 6)) { RollbackArm(); return false; }
        _detourRoll = true;
        if (waCode != null)
        {
            if (!InstallJump(_caveWa, waRva, WA_COVER)) { RollbackArm(); return false; }
            _detourWa = true;
        }
        if (wa2Code != null)
        {
            if (!InstallJump(_caveWa2, wa2Rva, WA2_COVER)) { RollbackArm(); return false; }
            _detourWa2 = true;
        }
        if (!InstallJump(_caveSpd, _armRva[4], 5)) { RollbackArm(); return false; }
        _detourSpd = true;

        LogArm("detour 已装：snap 0x" + _armRva[1].ToString("X") + "(10B) exit 0x" + _armRva[2].ToString("X")
            + "(5B) entry 0x" + _armRva[0].ToString("X") + "(5B) roll 0x" + _armRva[3].ToString("X")
            + "(6B) spd 0x" + _armRva[4].ToString("X") + "(5B)"
            + (bbCode != null
                ? " bobacc 0x" + bobAccRva.ToString("X") + "(5B→跳过至 0x" + bobAccSkipRva.ToString("X") + ")"
                : " bobacc 未安装（2 档将退回不跳过模式）")
            + (waCode != null
                ? " wa 0x" + waRva.ToString("X") + "(6B→2 档清 xmm6 掐 bob 累加)"
                : " wa 未安装（2 档最终变换无法接管）")
            + (wa2Code != null
                ? " wa2 0x" + wa2Rva.ToString("X") + "(5B→2 档清 xmm3/4/5 掐 pose 累加)"
                : " wa2 未安装（2 档直通分支仍会晃）"));
        return true;
    }

    private void RollbackArm()
    {
        if (_detourBobAcc)
        {
            WriteCode(_base + (_armRva[2] - 0xA9), new byte[] { 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30 });
            _detourBobAcc = false;
        }
        if (_detourExit) { WriteCode(_base + _armRva[2], RestoreFromSig("arm.exit", _armRva[2], 5)); _detourExit = false; }
        if (_detourRoll) { WriteCode(_base + _armRva[3], RestoreFromSig("arm.roll", _armRva[3], 6)); _detourRoll = false; }
        if (_detourWa) { WriteCode(_base + (_armRva[2] + WA_OFF_FROM_EXIT), new byte[] { 0xF3, 0x0F, 0x10, 0x64, 0x24, 0x34 }); _detourWa = false; }
        if (_detourWa2) { WriteCode(_base + (_armRva[2] + WA2_OFF_FROM_EXIT), new byte[] { 0xF3, 0x0F, 0x10, 0x5C, 0x24, 0x30 }); _detourWa2 = false; }
        if (_detourSnap) { WriteCode(_base + _armRva[1], RestoreFromSig("arm.snap", _armRva[1], 10)); _detourSnap = false; }
        if (_detourEntry) { WriteCode(_base + _armRva[0], RestoreFromSig("arm.entry", _armRva[0], 5)); _detourEntry = false; }
        if (_detourSpd) { WriteCode(_base + _armRva[4], RestoreFromSig("arm.spd", _armRva[4], 5)); _detourSpd = false; }
        LogArm("安装中断，已回滚已写入的 hook 点");
    }

    private void UninstallArm()
    {
        if (_detourBobAcc)
        {
            WriteCode(_base + (_armRva[2] - 0xA9), new byte[] { 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x30 });
            _detourBobAcc = false;
        }
        if (_detourExit) { WriteCode(_base + _armRva[2], RestoreFromSig("arm.exit", _armRva[2], 5)); _detourExit = false; }
        if (_detourRoll) { WriteCode(_base + _armRva[3], RestoreFromSig("arm.roll", _armRva[3], 6)); _detourRoll = false; }
        if (_detourWa) { WriteCode(_base + (_armRva[2] + WA_OFF_FROM_EXIT), new byte[] { 0xF3, 0x0F, 0x10, 0x64, 0x24, 0x34 }); _detourWa = false; }
        if (_detourWa2) { WriteCode(_base + (_armRva[2] + WA2_OFF_FROM_EXIT), new byte[] { 0xF3, 0x0F, 0x10, 0x5C, 0x24, 0x30 }); _detourWa2 = false; }
        if (_detourSnap) { WriteCode(_base + _armRva[1], RestoreFromSig("arm.snap", _armRva[1], 10)); _detourSnap = false; }
        if (_detourEntry) { WriteCode(_base + _armRva[0], RestoreFromSig("arm.entry", _armRva[0], 5)); _detourEntry = false; }
        if (_detourSpd) { WriteCode(_base + _armRva[4], RestoreFromSig("arm.spd", _armRva[4], 5)); _detourSpd = false; }
        LogArm("已恢复手臂通道 6 个 hook 点的原始字节");
    }

    /// <summary>
    /// 用签名重建某点的原始字节：固定位取签名值；通配位从当前内存读（若已被 E9 覆盖则不可靠，
    /// 因此先按「首字节是否 E9」判断是否需要还原，非 E9 表示未被本插件改写）。
    /// </summary>
    private byte[] RestoreFromSig(string sigName, int rva, int len)
    {
        SigResult s = _loc.Get(sigName);
        byte[] cur = ReadBytes(rva, len);
        byte[] outp = new byte[len];
        for (int i = 0; i < len; i++) outp[i] = (s != null && s.Mask[i] != 0) ? s.Pat[i] : cur[i];
        return outp;
    }

    private bool InstallJump(IntPtr cave, int rvaHook, int cover)
    {
        IntPtr at = _base + rvaHook;
        long rel = cave.ToInt64() - (at.ToInt64() + 5);
        if (rel < int.MinValue || rel > int.MaxValue) { LogArm("cave 距离过远 @0x" + rvaHook.ToString("X")); return false; }

        byte[] det = new byte[cover];
        for (int i = 0; i < cover; i++) det[i] = 0x90;
        det[0] = 0xE9;
        BitConverter.GetBytes((int)rel).CopyTo(det, 1);
        if (!WriteCode(at, det)) { LogArm("装 detour 失败 @0x" + rvaHook.ToString("X")); return false; }
        return true;
    }

    private static byte[] BuildArmCave(byte[] template, long caveAddr, long shared, long jumpTo, int jmpOff, long abs1 = 0)
    {
        byte[] code = (byte[])template.Clone();
        byte[] imm = BitConverter.GetBytes(shared);

        for (int i = 0; i + 9 < code.Length; i++)
        {
            if (code[i] == 0x49 && code[i + 1] == 0xBB && code[i + 2] == 0x88 && code[i + 3] == 0x77 &&
                code[i + 4] == 0x66 && code[i + 5] == 0x55 && code[i + 6] == 0x44 &&
                code[i + 7] == 0x33 && code[i + 8] == 0x22 && code[i + 9] == 0x11)
            {
                imm.CopyTo(code, i + 2);
            }
        }

        if (abs1 != 0)
        {
            byte[] imm1 = BitConverter.GetBytes(abs1);
            for (int i = 0; i + 9 < code.Length; i++)
            {
                if (code[i] == 0x48 && code[i + 1] == 0xB8 && code[i + 2] == 0x99 && code[i + 3] == 0x88 &&
                    code[i + 4] == 0x77 && code[i + 5] == 0x66 && code[i + 6] == 0x55 &&
                    code[i + 7] == 0x44 && code[i + 8] == 0x33 && code[i + 9] == 0x22)
                {
                    imm1.CopyTo(code, i + 2);
                }
            }
        }

        long next = caveAddr + jmpOff + 5;
        long rel = jumpTo - next;
        if (rel < int.MinValue || rel > int.MaxValue)
        {
            LogArm("cave 内 jmp 距离过远 rel=" + rel);
            return null;
        }
        // v1.39.3：jmpOff 处必须是 jmp 操作码。模板偏移一旦写错（多插/漏插字节），
        //   这里会把 rel32 写进别的指令体内，运行期无声崩游戏 —— 所以构造阶段就拦死。
        if (code[jmpOff] != 0xE9 || jmpOff + 5 > code.Length)
        {
            LogArm("cave 模板自检失败：jmpOff=0x" + jmpOff.ToString("X") + " 处为 0x"
                + code[jmpOff].ToString("X") + "（应为 0xE9），len=" + code.Length + " → 拒绝构造");
            return null;
        }
        BitConverter.GetBytes((int)rel).CopyTo(code, jmpOff + 1);

        // v1.42.1：占位残留自检 —— 模板里只要还有未替换的 48 B8 假地址，
        //   call rax 就必然跳到非法地址（v1.42.0 选边闪退就是这条）。
        //   宁可不挂 detour，也不把野指针写进游戏主循环。
        for (int i = 0; i + 9 < code.Length; i++)
        {
            if (code[i] == 0x48 && code[i + 1] == 0xB8 && code[i + 2] == 0x99 &&
                code[i + 3] == 0x88 && code[i + 4] == 0x77 && code[i + 5] == 0x66 &&
                code[i + 6] == 0x55 && code[i + 7] == 0x44 && code[i + 8] == 0x33 &&
                code[i + 9] == 0x22)
            {
                LogArm("cave 占位自检失败：0x" + i.ToString("X") + " 处 movabs rax 仍为占位值"
                    + "（abs1=0x" + abs1.ToString("X") + "）→ 拒绝构造，避免 call 野指针");
                return null;
            }
        }
        return code;
    }

    private static string StateNameArm(int s)
    {
        switch (s)
        {
            case 0: return "CS2 原版（放行）";
            case 1: return "legacy 独立驱动（快照+覆盖）";
            case 2: return "完全不晃（位置冻结）";
            default: return "未知";
        }
    }

    private void ApplyStateArm(int s)
    {
        // v1.56.4：cl_bob=2 先完全按 1 档接管——把生效档位映射成 1，之后 _inState / SH_STATE /
        // 波形步进全走 1 档同一条通路（快照 + 覆盖 + EXIT 站写回 base），不再进入任何 2 档
        // 归零/冻结分支。cvar 原值仍留在 _state 里供日志对照（state=2 eff=1）。
        _effState = (s == 2) ? 1 : s;
        s = _effState;
        _inState = s;
        _bobTime = 0.0;
        _spd = 0f;
        _bobAmp = 0f;
        _bobLr = 0f;
        _bobRoll = 0f;
        _bobFb = 0f;
        _bobPitch = 0f;
        _bobYaw = 0f;

        if (_shared != IntPtr.Zero)
        {
            Marshal.WriteInt32(_shared, SH_STATE, s);
            if (s != 1)
            {
                WriteF(_shared, SH_BOB, 0f);
                WriteF(_shared, SH_BOB_LR, 0f);
                WriteF(_shared, SH_FB, 0f);
                WriteF(_shared, SH_LOW, 0f);
                WriteF(_shared, SH_ROLL, 0f);
                WriteF(_shared, SH_PITCH, 0f);
                WriteF(_shared, SH_YAW, 0f);
            }
        }
        _frzN = 0; _frzValid = false;
        if (_shared != IntPtr.Zero) Marshal.WriteInt32(_shared, SH_FRZ_OK, 0);
        for (int i = 0; i < 6; i++) { _frzAcc[i] = 0f; _frz[i] = 0f; }
        // v1.35.0：切到 2 档立刻用当前 base 播种并置 FRZ_OK，避免 cave 在首拍读到 0 把枪拽到原点
        if (s == 2 && _shared != IntPtr.Zero)
        {
            float[] seed = { F(SH_BASE_LR), F(SH_BASE_FB), F(SH_BASE_UD),
                             F(SH_BASE_LOW), F(SH_BASE_ANG_Y), F(SH_BASE_ANG_Z) };
            for (int i = 0; i < 6; i++) { _frzAcc[i] = seed[i]; _frz[i] = seed[i]; }
            _frzN = 1;
            WriteF(_shared, SH_FRZ_LR, _frz[0]);
            WriteF(_shared, SH_FRZ_FB, _frz[1]);
            WriteF(_shared, SH_FRZ_UD, _frz[2]);
            WriteF(_shared, SH_FRZ_LOW, _frz[3]);
            WriteF(_shared, SH_FRZ_ANG_Y, _frz[4]);
            WriteF(_shared, SH_FRZ_ANG_Z, _frz[5]);
            _frzValid = true;
            Marshal.WriteInt32(_shared, SH_FRZ_OK, 1);
            // v1.41.0：2 档只钉「低值/下压」这一个槽，静止值取 0（= 不叠加任何 bob 下压）
            WriteF(_shared, SH_STILL_LOW, 0f);
        }
        // v1.56.5：切档后立刻按新档位口径重取参数（2 档 = 后台常量，1 档 = 游戏内 cvar）
        RefreshParamsArm();
        LogArm("apply " + s + ": " + StateNameArm(s) + "（未改写任何代码字节）"
            + " 参数源=" + (_pSrcBg ? "后台常量" : "游戏内 cvar")
            + " v=" + _inVert.ToString("0.###") + " l=" + _inLat.ToString("0.###")
            + " low=" + _inLower.ToString("0.###") + " cyc=" + _inCycle.ToString("0.###")
            + " up=" + _inBobup.ToString("0.###"));
    }

    // ================================================================ 镜框通道初始化
    private void TryInitScope()
    {
        if (_loc == null || !_loc.TextMapped)
        {
            LogScope("通道 scope 跳过：定位层不可用（.text 未映射）");
            return;
        }

        SigResult s = _loc.Get("scope.render");
        if (s == null || !s.Unique)
        {
            LogScope("通道 scope 签名未命中，已跳过，其余通道继续：" + (s == null ? "缺失" : s.Reason));
            return;
        }

        _sRvaRender = s.ResolvedRva;
        if (!_loc.InText(_sRvaRender))
        {
            LogScope("通道 scope 解析出的 RVA 0x" + _sRvaRender.ToString("X") + " 不在 .text 内 → 跳过");
            return;
        }
        LogScope("签名解析 RVA 0x" + _sRvaRender.ToString("X") + "（基准 0xCB1370，差值 "
            + AobLocator.Signed(_sRvaRender - 0xCB1370) + "）");

        if (PROBE_MODE)
        {
            // v1.31.0：装探针（0xC93886 站点承载 1 档覆盖通道），随后按 cvar 实际值生效一次
            _sShared = Marshal.AllocHGlobal(SSH_SIZE);
            for (int i = 0; i < SSH_SIZE; i++) Marshal.WriteByte(_sShared, i, 0);
            _scopeReady = true;
            _sState = 0;
            InstallProbes();

            int swProbe = (int)Math.Round(ReadCvar(CI_SNIPER));
            if (swProbe < 0) swProbe = 0; else if (swProbe > 2) swProbe = 2;
            LogScope("探针版就绪，读取 cl_bob_sniper=" + swProbe);
            ApplyStateScope(swProbe);
            _sHzStamp = _sw.Elapsed.TotalSeconds;
            return;
        }

        if (!VerifyHookBytes("scope.render", _sRvaRender, 7))
        {
            LogScope("通道 scope 跳过：入口原字节校验不通过");
            return;
        }
        LogScope("入口原字节校验通过：" + Hex(ReadBytes(_sRvaRender, 7)) + "（+8 字节快照 "
            + Hex(_sEntryOrig8 = ReadBytes(_sRvaRender, 8)) + "）");

        _sShared = Marshal.AllocHGlobal(SSH_SIZE);
        for (int i = 0; i < SSH_SIZE; i++) Marshal.WriteByte(_sShared, i, 0);

        _sCave = AllocNear(_base, 0x1000);
        if (_sCave == IntPtr.Zero) { LogScope("cave 分配失败（2GB 内无可用地址）"); return; }

        byte[] code = (byte[])CAVE_SCOPE_TEMPLATE.Clone();
        BitConverter.GetBytes(_sShared.ToInt64()).CopyTo(code, SCAVE_IMM_OFF);
        long next = _sCave.ToInt64() + SCAVE_JMP_OFF + 5;
        long rel = (_base.ToInt64() + _sRvaRender + 7) - next;
        if (rel < int.MinValue || rel > int.MaxValue) { LogScope("cave 距回流点过远，rel32 不可达"); return; }
        BitConverter.GetBytes((int)rel).CopyTo(code, SCAVE_JMP_OFF + 1);
        Marshal.Copy(code, 0, _sCave, code.Length);
        FlushInstructionCache(GetCurrentProcess(), _sCave, (UIntPtr)code.Length);
        LogScope("cave @ 0x" + _sCave.ToInt64().ToString("X") + "（" + code.Length + "B，回流 0x"
            + (_base.ToInt64() + _sRvaRender + 7).ToString("X") + "），shadow @ 0x" + _sShared.ToInt64().ToString("X"));

        _scopeReady = true;
        RefreshParamsScope();
        _sState = 0;   // 先置 0，下面按 cvar 实际值触发一次 ApplyStateScope
        int sw0 = (int)Math.Round(ReadCvar(CI_SNIPER));
        if (sw0 < 0) sw0 = 0; else if (sw0 > 2) sw0 = 2;
        _sState = sw0;
        ApplyStateScope(_sState);
        _sHzStamp = _sw.Elapsed.TotalSeconds;
        LogScope("通道 scope 就绪，cl_bob_sniper=" + _sState + " → " + StateNameScope(_sState));
    }

    private void ApplyStateScope(int s)
    {
        _sInState = s;
        _sBobTime = 0.0;
        _sSpd = 0f;

        if (PROBE_MODE)
        {
            // v1.31.0：只有 0/1 两档；1 档在偏置站覆盖 rdi+0x0C / +0x10
            if (_pShared == IntPtr.Zero || !_probesReady)
            {
                _sState = 0;
                LogScope("cl_bob_sniper=" + s + " 未生效：探针未就绪");
                return;
            }
            if (s == 1)
            {
                SetBiasSites(true);
                _sState = 1;
                LogScope("cl_bob_sniper=1：覆盖式 legacy —— 镜框抖动改由插件单源 vert/lat 驱动"
                    + "（X←lat×" + SCOPE_LEGACY_UNIT_LAT.ToString("0.#") + "×" + SCOPE_GAIN.ToString("0.#")
                    + "、Y←(vert-直流)×" + SCOPE_LEGACY_UNIT_VERT.ToString("0.#") + "×" + SCOPE_GAIN.ToString("0.#")
                    + "），与手臂同相位");
                return;
            }
            // v1.48.0：2 档恢复 —— 闸门开、每帧覆盖值恒 0，镜框完全静止（不开镜抖动，开镜也不抖）
            if (s == 2)
            {
                SetBiasSites(true);
                _sState = 2;
                LogScope("cl_bob_sniper=2：覆盖式静止 —— 偏置站每帧写 0，镜框固定不动（手臂仍按 cl_bob 抖）");
                return;
            }
            SetBiasSites(false);   // 0 档：闸门关，原生 movss 直通，字节级等同 CS2 原版
            _sState = 0;
            LogScope("cl_bob_sniper=0：闸门关闭，偏置站直通，镜框按 CS2 原版绘制");
            return;
        }

        if (s == 0)
        {
            if (_sShared != IntPtr.Zero)
            {
                Marshal.WriteInt32(_sShared, SSH_DX, 0);
                Marshal.WriteInt32(_sShared, SSH_DY, 0);
            }
            UninstallScope();
            LogScope("apply 0：入口已恢复原始字节，CS2 原版行为（hooked=" + _sHooked + "）");
            return;
        }

        if (!InstallScopeHook())
        {
            LogScope("apply " + s + "：hook 安装失败，回退到 0");
            _sState = 0; _sInState = 0;
            return;
        }

        // v1.21.7：base 交给 cave 首帧锁存（旧版此处读取 NATX，此刻 cave 尚未执行，锁到的是 0）
        Marshal.WriteInt32(_sShared, SSH_BASEX, 0);
        Marshal.WriteInt32(_sShared, SSH_BASEY, 0);
        Marshal.WriteInt32(_sShared, SSH_DX, 0);
        Marshal.WriteInt32(_sShared, SSH_DY, 0);
        Marshal.WriteInt32(_sShared, SSH_ADOPT, 0);
        LogScope("apply " + s + "：ADOPT=0 等待首帧锁存 rect base，Δ=(0,0)");
    }

    private bool InstallScopeHook()
    {
        if (_sHooked) return true;
        if (_base == IntPtr.Zero || _sCave == IntPtr.Zero) return false;

        IntPtr at = _base + _sRvaRender;
        long rel = _sCave.ToInt64() - (at.ToInt64() + 5);
        if (rel < int.MinValue || rel > int.MaxValue) { LogScope("rel32 不可达"); return false; }

        byte[] patched = new byte[7];
        patched[0] = 0xE9;
        BitConverter.GetBytes((int)rel).CopyTo(patched, 1);
        patched[5] = 0x90;
        patched[6] = 0x90;

        if (!AtomicWrite8(at, patched)) { LogScope("安装 detour 失败（原子写）"); return false; }
        _sHooked = true;
        LogScope("detour 已装 0x" + _sRvaRender.ToString("X") + "：原字节 " + Hex(ReadBytes(_sRvaRender, 7))
            + " → " + Hex(patched) + "（rel32=0x" + rel.ToString("X") + "，cave 0x" + _sCave.ToInt64().ToString("X") + "）");
        return true;
    }

    private void UninstallScope()
    {
        if (!_sHooked || _base == IntPtr.Zero) return;
        IntPtr at = _base + _sRvaRender;
        byte[] cur = ReadBytes(_sRvaRender, 7);
        if (cur[0] != 0xE9)
        {
            LogScope("回滚跳过：首字节已非 E9（当前 " + Hex(cur) + "）");
            _sHooked = false;
            return;
        }
        byte[] orig = RestoreFromSig("scope.render", _sRvaRender, 7);
        if (!AtomicWrite8(at, orig)) { LogScope("回滚失败（原子写）"); return; }
        _sHooked = false;
        LogScope("已回滚原始字节 " + Hex(orig) + " @0x" + _sRvaRender.ToString("X"));
    }

    private unsafe bool AtomicWrite8(IntPtr at, byte[] seven)
    {
        if (!VirtualProtect(at, (UIntPtr)16, PAGE_EXECUTE_READWRITE, out uint old))
        {
            LogScope("VirtualProtect 失败 err=" + Marshal.GetLastWin32Error());
            return false;
        }

        bool ok = true;
        try
        {
            byte[] full = new byte[8];
            Buffer.BlockCopy(seven, 0, full, 0, seven.Length >= 7 ? 7 : seven.Length);
            full[7] = Marshal.ReadByte(at, 7);

            if ((at.ToInt64() & 7L) == 0)
            {
                long v = BitConverter.ToInt64(full, 0);
                Interlocked.Exchange(ref *(long*)at.ToPointer(), v);
                FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)8);
            }
            else
            {
                LogScope("警告：入口非 8 字节对齐，退化为逐字节写");
                for (int i = 0; i < 7; i++) Marshal.WriteByte(at, i, seven[i]);
                FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)7);
            }
        }
        catch (Exception ex)
        {
            LogScope("原子写异常：" + ex.GetType().Name + " " + ex.Message);
            ok = false;
        }

        VirtualProtect(at, (UIntPtr)16, old, out _);
        return ok;
    }

    private string StateNameScope(int s)
    {
        switch (s)
        {
            case 0: return "CS2 原版（不 hook，字节级等价）";
            case 1: return "legacy 行为（单源覆盖，幅度 ×" + SCOPE_GAIN.ToString("0.#") + " 固定增益）";
            case 2: return "静止覆盖（偏置站恒写 0，镜框完全不动）";
            default: return "未知";
        }
    }

    private void RefreshParamsScope()
    {
        // v1.56.7：cl_bob=2 时镜框参数也并入后台参数包（全 0），不读游戏内 cvar
        if (_state == 2)
        {
            _sVert = BG2_VERT; _sLat = BG2_LAT; _sCycle = BG2_CYCLE; _sBobup = BG2_BOBUP;
            return;
        }
        float v = ReadCvar(CI_VERT); if (v > 0f) _sVert = v;
        float l = ReadCvar(CI_LAT); if (l > 0f) _sLat = l;
        float c = ReadCvar(CI_CYCLE); if (c > 0f) _sCycle = c;
        float u = ReadCvar(CI_UP); if (u > 0f) _sBobup = u;
    }

    // ================================================================ 通道隔离自检
    private void CheckChannelOverlap()
    {
        List<string> names = new List<string>();
        List<int> starts = new List<int>();
        List<int> lens = new List<int>();

        if (_armReady)
        {
            int[] cover = { 5, 10, 5, 6, 5 };
            string[] nm = { "arm.entry", "arm.snap", "arm.exit", "arm.roll", "arm.spd" };
            for (int i = 0; i < 5; i++) { names.Add(nm[i]); starts.Add(_armRva[i]); lens.Add(cover[i]); }
        }
        if (_scopeReady) { names.Add("scope.render"); starts.Add(_sRvaRender); lens.Add(7); }

        if (names.Count == 0) return;

        for (int i = 0; i < starts.Count; i++)
        {
            for (int k = i + 1; k < starts.Count; k++)
            {
                if (starts[i] < starts[k] + lens[k] && starts[k] < starts[i] + lens[i])
                {
                    Log("检测到 hook 区间重叠（" + names[i] + "@0x" + starts[i].ToString("X") + " / "
                        + names[k] + "@0x" + starts[k].ToString("X")
                        + "）→ 关停重叠通道，拒绝重复 patch 同一地址");
                    _abort = true;
                    return;
                }
            }
        }
        Log("通道隔离自检通过：" + string.Join(" / ", names.ToArray())
            + " 共 " + names.Count + " 个 hook 点，区间无重叠");
    }

    // ================================================================ 后台波形线程
    private void StartWaveThread()
    {
        if (_waveThread != null) return;
        _waveRun = true;
        _waveThread = new Thread(WaveLoop) { IsBackground = true, Name = "BobCoreWave", Priority = ThreadPriority.AboveNormal };
        _waveThread.Start();
        Log("波形线程已启动（1kHz，两条通道共用一条线程）");
    }

    private void WaveLoop()
    {
        long freq = Stopwatch.Frequency;
        long step = Math.Max(1L, freq / 1000L);
        long next = Stopwatch.GetTimestamp();
        double last = (double)next / freq;

        while (_waveRun)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < next) { Thread.SpinWait(40); continue; }
            next += step;
            if (now - next > step * 40) next = now + step;

            double t = (double)now / freq;
            float dt = (float)(t - last);
            last = t;
            if (dt <= 0f || dt > 0.2f) dt = 0.001f;

            try
            {
                if (_armReady)
                {
                    PollStateOffline();
                    ProbeNative();
                    StepWaveArm(dt);
                }
                if (_scopeReady)
                {
                    StepWaveScope(dt);
                }
            }
            catch (Exception ex)
            {
                Log("波形线程异常：" + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private void ProbeNative()
    {
        if (_shared == IntPtr.Zero) return;
        int seq = Marshal.ReadInt32(_shared, SH_SPD_SEQ);
        if (seq != _nSeq)
        {
            _nSeq = seq;
            _nStamp = _sw.Elapsed.TotalSeconds;
            _nSpd = F(SH_SPD_N);
            _nVm = Marshal.ReadInt32(_shared, SH_VM) != 0;
        }
        _useNative = (_sw.Elapsed.TotalSeconds - _lastTickStamp) > 0.30;
    }

    private void PollStateOffline()
    {
        if (!_inited || _abort || _shared == IntPtr.Zero) return;
        double now = _sw.Elapsed.TotalSeconds;
        if (now - _lastTickStamp < 0.30) return;
        if (now - _pollStamp < 0.20) return;
        _pollStamp = now;
        try
        {
            float v = ReadCvar(CI_BOB);
            if (v < -0.5f) return;
            int want = (int)Math.Round(v);
            if (want < 0) want = 0; else if (want > 2) want = 2;
            if (want != _state) { _state = want; ApplyStateArm(want); LogArm("离线模式切换 " + want + (want == 2 ? "（按 1 档接管）" : "") + "：" + StateNameArm(_effState)); }
        }
        catch { }
    }

    // ================================================================ v1.58.0：引擎时间源
    /// <summary>
    /// 解析引擎时钟：从 arm.spd 前 0x10 的 call 现算「取本帧时间」函数，再从函数体里的
    /// mov rbx,[rip+rel32] 现算时钟对象全局。全部现算、不写死地址，跨版本也只需签名仍命中。
    /// </summary>
    private void ResolveEngineClock()
    {
        _engPtrRva = -1; _engClock = IntPtr.Zero; _lastEngT = -1.0; _engJumps = _engStalls = 0;
        _phaseRate = 1.0; _engStill = 0.0;  _phaseAcc = 0.0; _phaseWallAcc = 0.0; _lastEngT = -1.0; _phaseEng = 0.0;
        try
        {
            int site = _armRva[4] - 0x10;                 // 0x4AADC9：call 0x18046C5C0（写 [rbp+0x50] = 本帧时间）
            byte[] sb = ReadBytes(site, 5);
            if (sb == null || sb.Length < 5 || sb[0] != 0xE8)
            {
                LogArm("引擎时钟：arm.spd-0x10 处不是 E8 call（实际 " + (sb == null ? "读取失败" : Hex(sb))
                    + "）→ 相位时间源退回墙钟");
                return;
            }
            int fn = CallTargetRva(site);
            if (fn <= 0 || !_loc.InText(fn))
            {
                LogArm("引擎时钟：取时间函数解析失败（0x" + fn.ToString("X") + "）→ 相位时间源退回墙钟");
                return;
            }

            // 函数体前 0x80 字节内找 mov rbx,[rip+rel32]（48 8B 1D）—— 时钟对象全局
            byte[] fb = ReadBytes(fn, 0x80);
            int ptrRva = -1;
            for (int i = 0; fb != null && i + 7 <= fb.Length; i++)
            {
                if (fb[i] != 0x48 || fb[i + 1] != 0x8B || fb[i + 2] != 0x1D) continue;
                int rel = BitConverter.ToInt32(fb, i + 3);
                int cand = fn + i + 7 + rel;
                if (cand > 0 && !_loc.InText(cand)) { ptrRva = cand; break; }   // 落在 .data 才算
            }
            if (ptrRva < 0)
            {
                LogArm("引擎时钟：在 0x" + fn.ToString("X") + " 里没找到时钟对象全局引用 → 相位时间源退回墙钟");
                return;
            }

            IntPtr obj = IntPtr.Zero;
            try { obj = Marshal.ReadIntPtr(_base, ptrRva); } catch { }
            if (obj != IntPtr.Zero)
            {
                float t0 = ReadF32(obj + ENG_CLOCK_OFF);
                if (float.IsNaN(t0) || float.IsInfinity(t0) || t0 < 0f || t0 > 1e7f)
                {
                    LogArm("引擎时钟：全局 0x" + ptrRva.ToString("X") + " +0x34 不像时间（" + t0.ToString("0.###")
                        + "）→ 相位时间源退回墙钟");
                    return;
                }
            }

            _engPtrRva = ptrRva;
            _engClock = obj;
            LogArm("引擎时钟已解析：取时间函数=0x" + fn.ToString("X") + " 时钟全局=0x" + ptrRva.ToString("X")
                + " 时间字段=+0x" + ENG_CLOCK_OFF.ToString("X")
                + " 当前 cur=" + (obj != IntPtr.Zero ? ReadF32(obj + ENG_CLOCK_OFF).ToString("0.000") : "对象未建，运行期再取"));
        }
        catch (Exception ex)
        {
            _engPtrRva = -1; _engClock = IntPtr.Zero;
            LogArm("引擎时钟解析异常：" + ex.GetType().Name + ": " + ex.Message + " → 相位时间源退回墙钟");
        }
    }

    private static float ReadF32(IntPtr addr)
    {
        try { return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(addr)); }
        catch { return float.NaN; }
    }

    /// <summary>
    /// 本帧引擎时间增量：暂停返回 0（相位冻结），倍速按倍速推进；
    /// 时间回退 / 跳变（换图、demo seek、从头播放）只重置基准、本帧返回 0。
    /// 未解析成功时退回墙钟 dt。
    /// </summary>
    private float EngineDelta(float wallDt)
    {
        if (wallDt <= 0f) wallDt = 0.001f;
        _wallAcc = _sw.Elapsed.TotalSeconds;

        _phaseWallAcc += wallDt;
        if (_phaseWallAcc >= 0.5)
        {
            _phaseRate = _phaseAcc / _phaseWallAcc;
            _phaseAcc = 0.0; _phaseWallAcc = 0.0;
        }

        // v1.58.9：引擎时钟（+0x34）**只用来判断「暂停 / 冻结」**，不再参与相位推进量的计算。
        //   实测该字段在 tick 之间会频繁回退再前进（engBk 每秒 300+ 次），任何逐帧差分都会失真：
        //   v1.58.6 收下正增量、丢掉负回跳 → engR=2.8（快 2.8 倍）；
        //   v1.58.8 丢掉回退采样 → 相位长时间卡住、攒够了再猛冲 → 观感「抖一阵停一阵」。
        //   现在相位推进量 = 墙钟帧时间（严格 1.0 倍速，均匀无阶梯），
        //   引擎时间只取**单调水位**：水位 150ms 不涨才认定真的暂停并冻结相位。
        bool frozen = false;
        if (_engPtrRva >= 0)
        {
            try
            {
                IntPtr obj = Marshal.ReadIntPtr(_base, _engPtrRva);
                if (obj != IntPtr.Zero)
                {
                    _engClock = obj;
                    float t = ReadF32(obj + ENG_CLOCK_OFF);
                    if (!(float.IsNaN(t) || float.IsInfinity(t) || t < 0f || t > 1e7f))
                    {
                        double tt = t;
                        if (_lastEngT < 0.0) { _lastEngT = tt; _lastMaxUpWall = _wallAcc; }
                        else if (tt > _lastEngT + 0.0005) { _lastEngT = tt; _lastMaxUpWall = _wallAcc; _engStill = 0.0; }
                        else if (tt < _lastEngT - 0.000001) _engBacks++;
                        frozen = (_wallAcc - _lastMaxUpWall) > 0.15;
                    }
                }
            }
            catch { _engPtrRva = -1; }
        }

        if (frozen) { _engStill += wallDt; return 0f; }
        _phaseEng += wallDt;
        _phaseAcc += wallDt;
        return wallDt;
    }

// ---------------------------------------------------------------- 手臂波形（原样搬移）
    private void StepWaveArm(float dt)
    {
        int st = _inState;

        float target = Clamp(_inSpeed, 0f, 320f);
        float rate = dt * 640f;
        float delta = target - _spd;
        if (delta > rate) delta = rate;
        if (delta < -rate) delta = -rate;
        _spd = Clamp(_spd + delta, 0f, 320f);

        // v1.43.0：把平滑速度（x100 整数）写进 shared+0x80，供 SNAP cave 做 2 档门控 ——
        //   速度 > 0.5 时冻结 base（不刷新），走动时手臂完全不晃；速度回落立即恢复刷新。
        if (_shared != IntPtr.Zero) Marshal.WriteInt32(_shared, SH_SPD_N, (int)(_spd * 100f));

        float speed = _spd;

        // v1.58.0：相位改由引擎时间驱动 —— demo 暂停即停、快进即倍速，不再用墙钟导致漂移
        float dte = EngineDelta(dt);
        if (dte > 0f) _bobTime += (double)dte * (speed * _pSpdInt);

        bool alive = _inAlive, hasVm = _inHasWeapon, air = _inAir;

        float lower = 0f;
        if (alive)
        {
            float lamt = _inLower * _pKLower;
            if (air && _pParams != 1) lamt *= _pAirLower;
            lower = speed * lamt * _pKLowerScale;
        }

        float maxSpeed = (_state == 2) ? BG2_MAXSPEED : _inMaxSpeed;
        float cyc;
        if (alive && hasVm && !air && maxSpeed > 1f)
            cyc = (1000f - maxSpeed) * CYC_K_GROUND;
        else
            cyc = CYC_FALLBACK_243;
        if (_pParams == 1) cyc *= _inCycle;
        if (!(cyc > 0.0001f)) cyc = 0.0001f;
        _lastCycle = cyc;
        _lastMaxSpeed = maxSpeed;

        bool manual = _pParams == 1;
        double pv = Frac(_bobTime / cyc);
        double pl = Frac(_bobTime / cyc * (manual ? 0.5 : 2.0));
        float bobup = manual ? _inBobup : BOBUP_243;
        double angleV = MapPhaseArm(pv, bobup);
        double angleL = manual ? pl * Math.PI * 2.0 : MapPhaseArm(pl, bobup);

        // v1.23.0：纠正 v1.22.0 的误判——0x10337DF0 是"句柄→实体指针"解析函数（内部 mov ecx,[ecx+0x150]
        // 再按 index/serial 校验），返回 0 表示没有关联实体，与 onGround 无关。
        // 所以 k 是 hasVm ? 0.00625 : 0.00125；空中不改变 k，也不改变幅度。
        float k = hasVm ? _pKAmp : _pKAmpNoEnt;

        // v1.22.0：legacy scoped 分支（0x106A3CBE）直接取硬编码 0.3 覆盖 amp_vert，不读 cl_bobamt_vert；
        // 非 scoped 才读 cvar，也没有"空中改 0.30"这层替换。
        // v1.23.2：0x106A3CB7 的判据是 viewmodel 的 m_bShouldIgnoreOffsetAndAccuracy（owner+0x29A0），
        // 命中才用硬编码 0.3 / 0.5 覆盖 amp；未命中照读 cl_bobamt_vert / cl_bobamt_lat，与 legacy 同一分支。
        // v1.56.6：2 档必须完全静止，因此屏蔽开镜硬编码 0.3/0.5 覆盖（否则 BG2_* = 0 会被它顶掉）。
        bool ignoreOff = _sScoped && _state != 2;
        float amtV = ignoreOff ? _pScopedVert : _inVert;
        float aV = speed * k * amtV;
        float shapeV = (float)(_pShapeBase + _pShapeGain * Math.Sin(angleV));
        float waveV = aV * shapeV;

        float lat = 0f, aL = 0f, shapeL = 0f;
        // v1.22.0：legacy 的 lat 通道（0x106A417D）只判"有实体/有武器"，从不做 onGround 门控；
        // 空中是靠 k=0.00125 自然衰减，不是把 lat 整路关掉。
        bool latActive = alive && hasVm;
        if (latActive)
        {
            aL = speed * k * (ignoreOff ? _pScopedLat : _inLat);
            shapeL = manual ? (float)Math.Sin(angleL) : (float)(_pShapeBase + _pShapeGain * Math.Sin(angleL));
            lat = Clamp(aL * shapeL, -_pClampLat, _pClampLat);
        }

        float vert = Clamp(waveV - lower, _pClampVertMin, _pClampVertMax);

        // v1.58.3：1 档幅度降一档 —— 在 vert / lat 源头统一乘 M1_AMP_SCALE，
        // 六条输出通道（fb/ud/lr/roll/pitch/yaw）与下方镜框广播一起缩，比例关系保持不变。
        float m1s = (st == 1) ? M1_AMP_SCALE : 1f;
        float vertO = vert * m1s;
        float latO = lat * m1s;

        float fb = vertO * _pAxFwd * _pOutScale;
        float ud = vertO * _pAxUp * _pOutScale;
        float lr = latO * _pAxRight * _pOutScale;
        float roll = vertO * _pAngRoll;
        float pitch = vertO * _pAngPitch;
        float yaw = latO * _pAngYaw;

        if (st != 1)
        {
            fb = 0f; ud = 0f; lr = 0f; roll = 0f; pitch = 0f; yaw = 0f;
        }

        // v1.23.0：把这一帧算出的 legacy bob 原始值（未乘轴系数/最终 0.4）作为唯一源对外广播，
        // 镜框通道直接消费它，保证"手抖多大镜子就晃多大"。st!=1（停用）时置 0。
        _bobVertRaw = st == 1 ? vertO : 0f;
        _bobLatRaw = st == 1 ? latO : 0f;
        // v1.29.0：vert = aV*(0.3+0.7sin) - lower 带正直流分量，镜框若直接拿来用会整体偏移（正是 v1.26 前
        // 「整个框移走」的原因）；这里广播直流分量，供镜框侧减掉，只保留 aV*0.7*sin 这一段零均值抖动。
        _bobVertDc = st == 1 ? ((aV * _pShapeBase - lower) * m1s) : 0f;
        _bobRawOk = true;

        _bobFb = fb;
        _bobAmp = ud;
        _bobLr = lr;
        _bobRoll = roll;
        _bobPitch = pitch;
        _bobYaw = yaw;

        if (_shared != IntPtr.Zero)
        {
            // v1.44.0：2 档「全数值调 0」——shared 里所有增量字段强制写 0，
            //   即使 bobacc 跳板某帧漏跳、或 1 档累加段被别的分支走到，也不会有增量残留。
            // v1.58.2：回退差值投喂（v1.58.1 实测证伪）。
            //   出口站每帧写的是 base（SNAP 站本帧抄的基准 pose）+ 增量槽；base 不含上一帧
            //   写进去的增量，所以「只喂差分」= 每帧只加一个极小差值 → 实战 cl_bob=1 几乎不晃
            //   （日志实测 out≈base，残差仅 0.02~0.5，v1.58.0 时能到 1.85）。
            //   恢复绝对波形投喂：out = base + wave，单源、每帧独立，不依赖任何累计。
            bool zero2 = st == 2;
            WriteF(_shared, SH_BOB, zero2 ? 0f : ud);
            WriteF(_shared, SH_BOB_LR, zero2 ? 0f : lr);
            WriteF(_shared, SH_FB, zero2 ? 0f : fb);
            WriteF(_shared, SH_LOW, 0f);
            WriteF(_shared, SH_ROLL, zero2 ? 0f : roll);
            WriteF(_shared, SH_PITCH, zero2 ? 0f : pitch);
            WriteF(_shared, SH_YAW, zero2 ? 0f : yaw);

            if (st == 2)
            {
                // v1.35.0：2 档改为「出口站绝对覆盖」，FRZ 必须从第一拍就有效，
                // 因此不再等 1200 样本锁定，直接跑滑动平均并每拍刷进共享内存。
                _frzAcc[0] += F(SH_BASE_LR);
                _frzAcc[1] += F(SH_BASE_FB);
                _frzAcc[2] += F(SH_BASE_UD);
                _frzAcc[3] += F(SH_BASE_LOW);
                _frzAcc[4] += F(SH_BASE_ANG_Y);
                _frzAcc[5] += F(SH_BASE_ANG_Z);
                if (_frzN < 1000000) _frzN++;
                for (int i = 0; i < 6; i++) _frz[i] = _frzAcc[i] / _frzN;
                WriteF(_shared, SH_FRZ_LR, _frz[0]);
                WriteF(_shared, SH_FRZ_FB, _frz[1]);
                WriteF(_shared, SH_FRZ_UD, _frz[2]);
                WriteF(_shared, SH_FRZ_LOW, _frz[3]);
                WriteF(_shared, SH_FRZ_ANG_Y, _frz[4]);
                WriteF(_shared, SH_FRZ_ANG_Z, _frz[5]);
                if (!_frzValid)
                {
                    _frzValid = true;
                    Marshal.WriteInt32(_shared, SH_FRZ_OK, 1);
                    LogArm("freeze | 2 档绝对覆盖生效（滑动平均 lr=" + _frz[0].ToString("0.000")
                        + " fb=" + _frz[1].ToString("0.000") + " ud=" + _frz[2].ToString("0.000")
                        + " low=" + _frz[3].ToString("0.000") + " angY=" + _frz[4].ToString("0.000")
                        + " angZ=" + _frz[5].ToString("0.000") + "）");
                }
            }
            else if (_frzN != 0 || _frzValid)
            {
                _frzN = 0; _frzValid = false;
                Marshal.WriteInt32(_shared, SH_FRZ_OK, 0);
                for (int i = 0; i < 6; i++) { _frzAcc[i] = 0f; _frz[i] = 0f; }
            }

            Marshal.WriteInt32(_shared, SH_STATE, st);
        }
    }

    // ---------------------------------------------------------------- 镜框波形（原样搬移）
    private void StepWaveScope(float dt)
    {
        int st = _sInState;

        // v1.29.0：每帧把覆盖值刷进两个偏置站（1 档 = 单源 legacy 值，2 档 = 0），保证镜框与手臂同帧同相位
        SyncBiasDelta();

        // v1.23.0：镜框不再自算速度/相位/幅度。legacy CalcViewModelBob 只算一份 vert/lat，
        // 写回 player（bobstate+0x14/+0x18）后 viewmodel 偏移与 CHudScope 镜框偏移都读它，
        // 所以开镜时手臂晃多大、镜子就晃多大；scoped 的 0.3/0.5 也在同一份数据里生效。
        float vert = _bobRawOk ? _bobVertRaw : 0f;
        float lat = _bobRawOk ? _bobLatRaw : 0f;

        if (st != 1) { vert = 0f; lat = 0f; }

        // v1.24.0：bob 值本身就是像素量级，1:1 直取（旧版 ×16 放大成 ±80px，即使生效也是糊的）。
        int dx = (int)Math.Round(lat, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(vert, MidpointRounding.AwayFromZero);
        dx = dx > 24 ? 24 : (dx < -24 ? -24 : dx);
        dy = dy > 24 ? 24 : (dy < -24 ? -24 : dy);

        if (_sShared != IntPtr.Zero)
        {
            Marshal.WriteInt32(_sShared, SSH_DX, dx);
            Marshal.WriteInt32(_sShared, SSH_DY, dy);
        }
    }

    private static double Frac(double x)
    {
        double f = x - Math.Floor(x);
        return f < 0.0 ? f + 1.0 : f;
    }

    private static double MapPhaseArm(double p, float bobup)
    {
        double up = bobup;
        if (up <= 0.0)
            return (p - up) * Math.PI / (1.0 - up) + Math.PI;
        if (up >= 1.0)
            return p * Math.PI / up;
        if (p < up) return p * Math.PI / up;
        return (p - up) * Math.PI / (1.0 - up) + Math.PI;
    }

    private static double MapPhaseScope(double p, float bobup)
    {
        double up = Math.Min(0.999, Math.Max(0.001, bobup));
        if (p < up) return p * Math.PI / up;
        return (p - up) * Math.PI / (1.0 - up) + Math.PI;
    }

    private static float Clamp(float v, float lo, float hi)
    {
        return v < lo ? lo : (v > hi ? hi : v);
    }

    // ================================================================ 输入采样
    private void RefreshParamsArm()
    {
        // v1.32.0：cl_bob_params 后台化，固定手动档（参数取自 cl_bobamt_vert/lat/lower_amt）
        _pParams = 1;

        // v1.56.5：2 档完全走后台常量，不读游戏内 cl_bobamt_vert / cl_bobamt_lat /
        // cl_bob_lower_amt / cl_bobcycle / cl_bobup —— 游戏内改这些指令对 2 档一律无效。
        if (_state == 2)
        {
            _pVert = BG2_VERT; _inVert = BG2_VERT;
            _pLat = BG2_LAT; _inLat = BG2_LAT;
            _pLower = BG2_LOWER; _inLower = BG2_LOWER;
            _pCycle = BG2_CYCLE; _inCycle = BG2_CYCLE;
            _pBobup = BG2_BOBUP; _inBobup = BG2_BOBUP;
            // v1.56.7：结构参数一并切到 BG2 参数包（全 0），手臂链路无任何硬编码残留
            _pKAmp = BG2_K_AMP; _pKAmpNoEnt = BG2_K_AMP_NOENT; _pSpdInt = BG2_SPD_INT;
            _pKLower = BG2_K_LOWER; _pKLowerScale = BG2_K_LOWER_SCALE; _pAirLower = BG2_AIR_LOWER;
            _pAxFwd = BG2_AX_FWD; _pAxUp = BG2_AX_UP; _pAxRight = BG2_AX_RIGHT; _pOutScale = BG2_OUT_SCALE;
            _pAngPitch = BG2_ANG_PITCH; _pAngYaw = BG2_ANG_YAW; _pAngRoll = BG2_ANG_ROLL;
            _pClampLat = BG2_CLAMP_LAT; _pClampVertMin = BG2_CLAMP_VERT_MIN; _pClampVertMax = BG2_CLAMP_VERT_MAX;
            _pShapeBase = BG2_SHAPE_BASE; _pShapeGain = BG2_SHAPE_GAIN;
            _pScopedVert = BG2_SCOPED_VERT; _pScopedLat = BG2_SCOPED_LAT;
            _pSrcBg = true;
            LogArm("2档参数包：vert=-0.2 lat=-0.2 lower=-10 cycle=0.98 up=0.5，结构常量=legacy 原值（K_AMP/K_LOWER/SPD_INT/AX/ANG/CLAMP/SHAPE/SCOPED），不吃游戏内 cvar");
            return;
        }
        _pSrcBg = false;
        // v1.56.7：1 档恢复 legacy 原始参数包（逐项等于改造前的写死值）
        _pKAmp = K_AMP; _pKAmpNoEnt = K_AMP_NOENT; _pSpdInt = K_SPEED_INT;
        _pKLower = K_LOWER; _pKLowerScale = K_LOWER_SCALE; _pAirLower = 0.1f;
        _pAxFwd = AX_FORWARD; _pAxUp = AX_UP; _pAxRight = AX_RIGHT; _pOutScale = OUT_SCALE;
        _pAngPitch = ANG_PITCH; _pAngYaw = ANG_YAW; _pAngRoll = ANG_ROLL;
        _pClampLat = 8f; _pClampVertMin = -7f; _pClampVertMax = 4f;
        _pShapeBase = 0.3f; _pShapeGain = 0.7f;
        _pScopedVert = LEGACY_SCOPED_VERT; _pScopedLat = LEGACY_SCOPED_LAT;

        float vert = ReadCvar(CI_VERT);
        float lat = ReadCvar(CI_LAT);
        float lower = ReadCvar(CI_LOW);
        float cycle = ReadCvar(CI_CYCLE);
        float bobup = ReadCvar(CI_UP);
        if (vert >= 0f) { _pVert = vert; _inVert = vert; }
        if (lat >= 0f) { _pLat = lat; _inLat = lat; }
        if (lower >= 0f) { _pLower = lower; _inLower = lower; }
        if (cycle > 0f) { _pCycle = cycle; _inCycle = cycle; }
        if (bobup >= 0f) { _pBobup = bobup; _inBobup = bobup; }

    }

    private void SampleInputsArm()
    {
        float speed = 0f;
        try
        {
            Vector vel;
            if (TryGetLocalVelocity(out vel))
                speed = (float)Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        }
        catch { }
        _inSpeed = speed;

        bool alive = false, hasWp = false, onGround = true;
        float maxSpd = 250f;
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn != null)
            {
                alive = p!.PawnIsAlive;
                var wp = pawn.WeaponServices?.ActiveWeapon?.Value;
                hasWp = wp != null;
                maxSpd = MaxSpeedOf(wp == null ? "" : (wp.DesignerName ?? ""));
                try
                {
                    uint fl = Convert.ToUInt32(pawn.Flags);
                    onGround = (fl & 1u) != 0;
                }
                catch { onGround = true; }
            }
        }
        catch { }

        _inAlive = alive;
        _inHasWeapon = hasWp;
        _inAir = alive && !onGround;
        _inMaxSpeed = maxSpd;

        // v1.23.0：开镜锁写在手臂通道里，但 scoped 探测原本挂在镜框采样上。
        // 镜框通道关掉时这里补一次，保证 cl_bob=1 单独跑也能正确锁 0.3/0.5。
        if (!_scopeReady)
        {
            bool sc = TryReadIgnoreOffsetAcc();
            if (sc != _sScoped)
            {
                _sScoped = sc;
                LogArm(sc
                    ? "抑制位=1 -> 幅度钉 legacy 硬编码 0.3 / 0.5（不读 cl_bobamt_*）"
                    : "抑制位=0 -> 幅度回到 cl_bobamt_vert / cl_bobamt_lat");
            }
        }
    }

    private void SampleInputsScope()
    {
        float speed = 0f, dir = 0f;
        try
        {
            Vector vel;
            if (TryGetLocalVelocityScope(out vel))
            {
                speed = (float)Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                dir = Clamp(TryDirSign(vel) / 250f, -1f, 1f);
            }
        }
        catch { }
        _sInSpeed = speed;
        _sInDir = dir;

        // v1.21.8：开镜检测（CS2 无 scoped 幅度锁，需自己补）
        bool sc = TryReadIgnoreOffsetAcc();
        if (sc != _sScoped)
        {
            _sScoped = sc;
            LogScope(sc
                ? "抑制位=1 -> 幅度改钉 legacy 硬编码 0.3 / 0.5（不读 cl_bobamt_*）"
                : "抑制位=0 -> 幅度回到 cl_bobamt_vert / cl_bobamt_lat");
        }

        // v1.47.0 诊断：选择目标变化时打印一次「各玩家 pawn/武器/观察目标」明细
        try
        {
            string tag = _selTag;
            int nowMs = Environment.TickCount;
            if (tag != _obsDiagTag && (uint)(nowMs - _obsDiagAt) > 700u)
            {
                _obsDiagTag = tag;
                _obsDiagAt = nowMs;
                DumpObsDiag(tag);
            }
        }
        catch { }
    }

    private string _obsDiagTag = "";
    private int _obsDiagAt;

    private void DumpObsDiag(string tag)
    {
        try
        {
            var sb = new StringBuilder("obs | sel=" + tag);
            var players = Utilities.GetPlayers();
            if (players != null)
            {
                foreach (var q in players)
                {
                    if (q == null || !q.IsValid) continue;
                    string pawnDesc = "null", wpn = "0";
                    try
                    {
                        var pw = q.Pawn?.Value;
                        if (pw != null)
                        {
                            pawnDesc = pw.GetType().Name + "@" + pw.Handle.ToInt64().ToString("X");
                            wpn = (pw.WeaponServices?.ActiveWeapon?.Value != null) ? "1" : "0";
                        }
                    }
                    catch { }
                    string obsDesc = "null";
                    try { var t = ObserverTargetOf(q); obsDesc = t == null ? "null" : ("#" + t.Slot + ":" + t.PlayerName); } catch { }
                    sb.Append(" || #").Append(q.Slot).Append(q.IsBot ? "bot" : "人")
                      .Append(":").Append(q.PlayerName)
                      .Append(" pawn=").Append(pawnDesc)
                      .Append(" wpn=").Append(wpn)
                      .Append(" obs=").Append(obsDesc);
                }
            }
            LogScope(sb.ToString());
        }
        catch { }
    }

    // v1.23.2：判据改回真正 legacy。legacy client.dll 0x106A3CB7 判的是 viewmodel 的
    // m_bShouldIgnoreOffsetAndAccuracy（owner+0x29A0），不是 pawn 的 m_bIsScoped（0x9974）。
    // 该位置位才把 amp 钉 0.3 / 0.5，否则照读 cl_bobamt_vert / cl_bobamt_lat，与 legacy 分支一致。
    // 取不到 viewmodel（无武器 / 实体无效）时按 legacy 视作未抑制，回落 cvar 幅度。
    private bool TryReadIgnoreOffsetAcc()
    {
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null || pawn.Handle == IntPtr.Zero) return false;

            var svc = pawn.ViewModelServices as CCSPlayer_ViewModelServices;
            if (svc == null) return false;
            var list = svc.ViewModel;
            if (list.Length == 0) return false;
            var vm = list[0].Value as CCSGOViewModel;
            if (vm == null)
            {
                if (!_sVmMissLogged)
                {
                    _sVmMissLogged = true;
                    LogScope("取不到 CCSGOViewModel（无武器 / 实体未就绪），按 legacy 未抑制处理，回落 cl_bobamt_*");
                }
                return false;
            }

            if (!_sScopedProbeOk)
            {
                _sScopedProbeOk = true;
                LogScope("晃动抑制判据走 CCSGOViewModel.m_bShouldIgnoreOffsetAndAccuracy（legacy owner+0x29A0）");
            }

            return vm.ShouldIgnoreOffsetAndAccuracy;
        }
        catch { }
        return false;
    }

    private static float MaxSpeedOf(string dn)
    {
        if (string.IsNullOrEmpty(dn)) return 250f;
        dn = dn.ToLowerInvariant();
        int us = dn.LastIndexOf('_');
        string tail = us >= 0 && us < dn.Length - 1 ? dn.Substring(us + 1) : dn;

        switch (tail)
        {
            case "awp": return 200f;
            case "ssg08": return 230f;
            case "scar20": return 215f;
            case "g3sg1": return 215f;
            case "ak47": return 215f;
            case "m4a1": return 225f;
            case "m4a1_silencer":
            case "silencer": return 225f;
            case "negev": return 220f;
            case "m249": return 195f;
            case "nova": return 220f;
            case "xm1014": return 215f;
            case "mag7": return 225f;
            case "sawedoff": return 210f;
            case "p90": return 230f;
            case "mp9": return 240f;
            case "mp7": return 220f;
            case "mp5sd": return 235f;
            case "ump45": return 230f;
            case "bizon": return 240f;
            case "mac10": return 240f;
            case "deagle": return 230f;
            case "revolver": return 220f;
            case "glock": return 240f;
            case "hkp2000": return 240f;
            case "usp_silencer":
            case "usp": return 240f;
            case "p250": return 220f;
            case "tec9": return 240f;
            case "fiveseven": return 240f;
            case "cz75a": return 240f;
            case "elite": return 240f;
            case "knife":
            case "knife_t": return 250f;
            case "taser": return 220f;
            default: return 250f;
        }
    }

    private bool TryGetLocalVelocity(out Vector result)
    {
        result = new Vector(0, 0, 0);
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null) return false;
            result = pawn.AbsVelocity;
            return true;
        }
        catch { }
        return false;
    }

    // v1.46.0：范围放开到「所有人」。原先这里遍历 Utilities.GetPlayers() 并 `if (p.IsBot) continue;`，
    // 只认真人控制台玩家 —— 观战 BOT / demo 观察目标时取不到速度，镜框通道 bob 恒为 0。
    // 现在与手臂通道统一走 PickLocal()：观察目标优先（人或 bot 均可），其次任意有 pawn 的玩家，
    // 都没有时退到最快 pawn。三条通道共用同一个 bob 源，避免手臂和镜框各跟一个人。
    private bool TryGetLocalVelocityScope(out Vector result)
    {
        result = new Vector(0, 0, 0);
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null) return false;
            result = pawn.AbsVelocity;
            return true;
        }
        catch { }
        return false;
    }

    private float TryDirSign(Vector v)
    {
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null) return 0f;
            var ang = pawn.EyeAngles;
            double yaw = ang.Y * Math.PI / 180.0;
            double fx = Math.Cos(yaw), fy = Math.Sin(yaw);
            return (float)(v.X * fx + v.Y * fy);
        }
        catch { }
        return 0f;
    }

    // v1.47.0：控制/观战 BOT 时，本地观战者自己的 Pawn 依然存在（无武器、速度 0），
    //   旧逻辑会把它当目标 → bob 源恒 0 → 镜框一动不动。
    //   现在按「观察目标 → 有武器的真人 → 有武器且最快的 pawn → bot → 最快」排序，
    //   并排除观战者自带的 CCSObserverPawn。
    private static CCSPlayerController? PickLocal()
    {
        CCSPlayerController? obsTarget = null, obsOwner = null, human = null, bot = null, fast = null, armed = null;
        float fastSpd = -1f, armedSpd = -1f;
        try
        {
            var players = Utilities.GetPlayers();
            if (players == null) { _selTag = "无players"; return null; }

            // v1.57.8 修复（cl_bob=1 乱抖根因）：本地人类活着且持械时，bob 源恒为自己。
            //   旧逻辑第一步就遍历「场上任意有 ObserverTarget 的玩家」并直接返回该被观战目标，
            //   于是开局几十秒后只要有 BOT 死亡开始观战，本地活着的第一人称玩家就被劫持：
            //   日志 sel=obs->bot#5:Xyp9x(由bot#2) / spd=109~210 而引擎原生 nspd=0，
            //   手臂按别人的跑动速度持续跑 sine → 「不走也抖」。
            //   自己真正死亡观战时（无持械 pawn）仍走原来的观战目标跟随分支。
            CCSPlayerController? me = null;
            foreach (var p in players)
            {
                if (p == null || !p.IsValid) continue;
                if (p.IsBot) continue;
                var mp = p.Pawn?.Value;
                if (mp == null) continue;
                if (IsObserverPawn(mp, p)) continue;
                if (!HasWeapon(mp)) continue;
                me = p; break;
            }
            if (me != null)
            {
                _selTag = TagOf("local", me);
                return me;
            }

            foreach (var p in players)
            {
                if (p == null || !p.IsValid) continue;
                var t = ObserverTargetOf(p);
                if (t == null || !t.IsValid) continue;
                if (t.Pawn?.Value == null) continue;
                obsTarget = t; obsOwner = p;
                break;
            }

            foreach (var p in players)
            {
                if (p == null || !p.IsValid) continue;
                var pawn = p.Pawn?.Value;
                if (pawn == null) continue;
                if (IsObserverPawn(pawn, p)) continue;      // 观战者自带的观察用 pawn 不是抖动源
                bool wpn = HasWeapon(pawn);
                float s = SpeedOf(pawn);
                if (!p.IsBot)
                {
                    if (human == null) human = p;
                    else if (!HasWeapon(human.Pawn?.Value) && wpn) human = p;
                }
                else bot ??= p;
                if (wpn && s > armedSpd) { armedSpd = s; armed = p; }
                if (s > fastSpd) { fastSpd = s; fast = p; }
            }
        }
        catch { }

        if (obsTarget != null)
        {
            _selTag = TagOf("obs->", obsTarget) + (obsOwner != null ? "(由" + (obsOwner.IsBot ? "bot" : "人") + "#" + obsOwner.Slot + ")" : "");
            return obsTarget;
        }
        if (human != null && (HasWeapon(human.Pawn?.Value) || armed == null))
        {
            _selTag = TagOf("local", human);
            return human;
        }
        if (armed != null)
        {
            _selTag = TagOf("armed", armed) + " spd=" + armedSpd.ToString("0.0");
            return armed;
        }
        if (human != null) { _selTag = TagOf("local", human); return human; }
        if (bot != null)
        {
            if (fast != null && fastSpd > 1f)
            { _selTag = TagOf("fast", fast) + " spd=" + fastSpd.ToString("0.0"); return fast; }
            _selTag = TagOf("bot", bot);
            return bot;
        }
        _selTag = "无pawn";
        return null;
    }

    private static bool IsObserverPawn(CBasePlayerPawn pawn, CCSPlayerController owner)
    {
        try
        {
            var op = owner.ObserverPawn?.Value;
            if (op != null && op.Handle == pawn.Handle) return true;
        }
        catch { }
        try { return pawn.GetType().Name.IndexOf("Observer", StringComparison.OrdinalIgnoreCase) >= 0; }
        catch { return false; }
    }

    private static bool HasWeapon(CBasePlayerPawn? pawn)
    {
        if (pawn == null) return false;
        try { return pawn.WeaponServices?.ActiveWeapon?.Value != null; }
        catch { return false; }
    }

    private static string TagOf(string kind, CCSPlayerController p)
    {
        try { return kind + (p.IsBot ? "bot" : "人") + "#" + p.Slot + ":" + p.PlayerName; }
        catch { return kind + "?"; }
    }

    private static float SpeedOf(CBasePlayerPawn pawn)
    {
        try
        {
            var v = pawn.AbsVelocity;
            return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
        }
        catch { return 0f; }
    }

    // v1.47.0 根因修复：ObserverServices 不在 CCSPlayerController 上，而在 **Pawn** 上
    //   （CBasePlayerPawn.ObserverServices），且 ObserverTarget 是 CHandle<CBaseEntity>，
    //   Value 拿到的是实体（CCSPlayerPawn），旧代码 `as CCSPlayerController` 永远为 null
    //   → 观战/控制 BOT 时观察目标解析不出来，PickLocal 退回「本地观战者自己」
    //   → 观战者 pawn 无武器、速度 0 → bob 源恒 0 → 镜框一动不动（像 cl_bob_sniper=2）。
    // 现在：从 pawn / observer pawn 取 ObserverServices.ObserverTarget，再按实体 handle
    //   回查是哪个玩家的 pawn；顺带兜底 controller.DesiredObserverTarget。
    private static CCSPlayerController? ObserverTargetOf(CCSPlayerController p)
    {
        IntPtr target = IntPtr.Zero;
        try
        {
            object? pawnObj = null;
            try { pawnObj = p.Pawn?.Value; } catch { }
            if (pawnObj == null) { try { pawnObj = p.ObserverPawn?.Value; } catch { } }

            var os = pawnObj != null ? MemberOf(pawnObj, "ObserverServices") : null;
            if (os != null)
            {
                var v = HandleValueOf(MemberOf(os, "ObserverTarget"));
                if (v is CCSPlayerController direct) return direct;
                if (v is CEntityInstance ent && ent.IsValid) target = ent.Handle;
            }
            if (target == IntPtr.Zero)
            {
                var v2 = HandleValueOf(MemberOf(p, "DesiredObserverTarget"));
                if (v2 is CCSPlayerController d2) return d2;
                if (v2 is CEntityInstance e2 && e2.IsValid) target = e2.Handle;
            }
        }
        catch { }

        if (target == IntPtr.Zero) return null;
        try
        {
            foreach (var q in Utilities.GetPlayers())
            {
                if (q == null || !q.IsValid) continue;
                try { var pq = q.Pawn?.Value; if (pq != null && pq.Handle == target) return q; } catch { }
                try { var oq = q.ObserverPawn?.Value; if (oq != null && oq.Handle == target) return q; } catch { }
            }
        }
        catch { }
        return null;
    }

    // 反射读属性/字段（含非公开），兼容不同 CSS 版本的成员命名差异
    private static object? MemberOf(object o, string name)
    {
        try
        {
            var t = o.GetType();
            var pi = t.GetProperty(name);
            if (pi != null) return pi.GetValue(o);
            var fi = t.GetField(name);
            if (fi != null) return fi.GetValue(o);
            foreach (var b in t.GetProperties())
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b.GetValue(o);
        }
        catch { }
        return null;
    }

    // CHandle<T> → 实体；非句柄则原样返回
    private static object? HandleValueOf(object? h)
    {
        if (h == null) return null;
        try
        {
            var pi = h.GetType().GetProperty("Value");
            if (pi != null)
            {
                var v = pi.GetValue(h);
                if (v != null) return v;
            }
        }
        catch { }
        return h;
    }

    // ================================================================ 诊断日志
    private void PoseLog()
    {
        if (_shared == IntPtr.Zero || (_state != 1 && _state != 2)) return;
        float b0 = F(SH_BASE_LR), b1 = F(SH_BASE_FB), b2 = F(SH_BASE_UD), b3 = F(SH_BASE_LOW);
        if (Math.Abs(b0 - _pb0) < 0.02f && Math.Abs(b1 - _pb1) < 0.02f &&
            Math.Abs(b2 - _pb2) < 0.02f && Math.Abs(b3 - _pb3) < 0.02f) return;
        double now = _sw.Elapsed.TotalSeconds;
        if (now - _lastPoseLog < 0.2) return;
        _lastPoseLog = now;
        _pb0 = b0; _pb1 = b1; _pb2 = b2; _pb3 = b3;
        LogArm("pose | base=" + b0.ToString("0.000") + "/" + b1.ToString("0.000") + "/" + b2.ToString("0.000") + "/" + b3.ToString("0.000")
            + " out=" + (b0 + _bobLr).ToString("0.000") + "/" + (b1 + _bobFb).ToString("0.000") + "/"
            + (b2 + _bobAmp).ToString("0.000") + "/" + (b3 + _bobPitch).ToString("0.000")
            + " ang=(" + _bobPitch.ToString("0.000") + "/" + _bobYaw.ToString("0.000") + "/" + _bobRoll.ToString("0.000") + ") spd="
            + _inSpeed.ToString("0.0") + " air=" + (_inAir ? 1 : 0) + " ign=" + (_sScoped ? 1 : 0)
            + " amp=" + (_sScoped ? "0.30/0.50" : _inVert.ToString("0.00") + "/" + _inLat.ToString("0.00")));
    }

    private float _pb0, _pb1, _pb2, _pb3;
    private double _lastPoseLog = -10.0;

    // ============================================================ v1.52.0 手臂晃动源定性探针
    //   目的：把「手臂还在晃」拆成三种可能并一次定性
    //     A) viewmodel 实体世界坐标（AbsOrigin）在动      → 位置通道随走路变化
    //     B) viewmodel 实体朝向（AbsRotation）在动        → 角度通道随走路变化
    //     C) 两者都不动，但视觉仍在晃                     → 晃动在骨骼/动画层（pose），
    //                                                        与 0x4AA730 的位置写入无关
    //   判定用 3 秒窗口内的「极差 span」：站立不动时应接近 0；走动时看哪一组先跳起来。
    private bool _vmHasLast;
    private float _voX, _voY, _voZ;
    private float _vaP, _vaYw, _vaR;
    private float _voMinX, _voMaxX, _voMinY, _voMaxY, _voMinZ, _voMaxZ;
    private float _vaMinP, _vaMaxP, _vaMinYw, _vaMaxYw, _vaMinR, _vaMaxR;
    private float _vmStepMax;
    private float _vmAngStepMax;
    private int _vmSamples;
    private string _vmErr = "";
    private string _vmCls = "-";
    private int _vmArrN = -1;
    private System.Reflection.PropertyInfo _vmPiVo, _vmPiEa;
    private bool _vmProbeInit;
    private Type _vT;
    private System.Reflection.MemberInfo _vMx, _vMy, _vMz;
    private string _vmSrcName = "-";

    // v1.54.0：第一人称 viewmodel 偏移（schema ref 属性，ViewModelOffsetX/Y/Z）
    private System.Reflection.MethodInfo _vmGx, _vmGy, _vmGz, _vmGfov;
    private bool _vmOffInit;
    private float _vfX, _vfY, _vfZ, _vfFov;
    private float _vfMinX, _vfMaxX, _vfMinY, _vfMaxY, _vfMinZ, _vfMaxZ, _vfMinFov, _vfMaxFov;
    private float _vfStepMax;
    private int _vfSamples;
    private string _vfErr = "";

    // v1.55.0：诊断 ViewModelServices 上 m_hViewModel 的原始 handle（只读 int，绝不解引用）
    private bool _vmHInit;
    private int _vmHOff1 = -1, _vmHOff2 = -1;
    private string _vmHDiag = "-";

    // v1.56.0：改走 CSS 已暴露的 CCSPlayer_ViewModelServices.ViewModel（Span<CHandle<CBaseViewModel>>），
    //   逐帧只读采样 viewmodel 实体的 AbsOrigin / AbsRotation 与动画层参数，
    //   用于判定手臂晃动来自「实体位置逐帧抖」还是「动画层推进」。
    private bool _vmeInit;
    private string _vmeCls = "-";
    private int _vmeIdx = -1;
    private long _vmeRaw = -1;
    private long _vmeSP = 0;
    private int _vmeValid;
    private float _vmeMinX, _vmeMaxX, _vmeMinY, _vmeMaxY, _vmeMinZ, _vmeMaxZ;
    private float _vmeMinP, _vmeMaxP, _vmeMinYw, _vmeMaxYw, _vmeMinR, _vmeMaxR;
    private float _vmeLX, _vmeLY, _vmeLZ, _vmeLP, _vmeLYw, _vmeLR;
    private float _vmeStepMax, _vmeRotStepMax;
    private float _vmeOX, _vmeOY, _vmeOZ, _vmeOP, _vmeOYw, _vmeOR;
    private bool _vmeHasLast;
    private int _vmeSamples;
    private string _vmeErr = "";
    private int _vmeLLay = -999, _vmeLSeq = -999, _vmeLPar = -999;
    private int _vmeDPar, _vmeDLay, _vmeDSeq;
    private bool _vmeIgnore;
    // v1.56.3：实体枚举路径缓存（不依赖 schema 名、不解 CHandle）
    private int _vmeEntIdx = -1;
    private string _vmeDump = "";

    private CBaseEntity _vmCached;
    private int _vmMissTicks;

    // 读 schema ref 属性（类型形如 Single&，getter 带一个 ref/out 参数），全托管，无指针解引用
    private static bool TryGetRefFloat(object target, System.Reflection.MethodInfo getter, out float val)
    {
        val = 0f;
        if (getter == null || target == null) return false;
        try
        {
            var ps = getter.GetParameters();
            var args = new object[ps.Length == 0 ? 0 : ps.Length];
            if (args.Length > 0) args[0] = 0f;
            getter.Invoke(target, args);
            if (args.Length > 0) { val = Convert.ToSingle(args[0]); return true; }
            val = Convert.ToSingle(getter.Invoke(target, null));
            return true;
        }
        catch { return false; }
    }

    private static float AngDiff(float a, float b)
    {
        float d = a - b;
        while (d > 180f) d -= 360f;
        while (d < -180f) d += 360f;
        return d;
    }

    // v1.53.1：不依赖具体类型，用反射读任意 {X,Y,Z} 成员（Vector / QAngle / Vector3 都适用）
    private static float ReadNum(object o, System.Reflection.MemberInfo m)
    {
        object v = (m is System.Reflection.PropertyInfo pi) ? pi.GetValue(o) : ((System.Reflection.FieldInfo)m).GetValue(o);
        if (v == null) return 0f;
        try { return Convert.ToSingle(v); } catch { return 0f; }
    }

    private bool TryVec(object o, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (o == null) return false;
        var t = o.GetType();
        if (!ReferenceEquals(_vT, t))
        {
            _vT = t;
            System.Func<string, System.Reflection.MemberInfo> pick = n =>
            {
                var pr = t.GetProperty(n);
                if (pr != null) return pr;
                return (System.Reflection.MemberInfo)t.GetField(n);
            };
            _vMx = pick("X"); _vMy = pick("Y"); _vMz = pick("Z");
        }
        if (_vMx == null || _vMy == null || _vMz == null) return false;
        x = ReadNum(o, _vMx); y = ReadNum(o, _vMy); z = ReadNum(o, _vMz);
        return true;
    }

    // v1.53.0：v1.52.3 的「schema 直读 m_hViewModel + CHandle 构造」实测导致游戏闪退。
    //   推断：GetSchemaOffset 在类名不匹配时回退取 CPlayer_ViewModelServices 的偏移，
    //   读出的 raw 未必是合法 CHandle，new CHandle<CBaseEntity>(raw).Value 及随后 AbsOrigin
    //   访问会落到非法内存触发 AV，托管 try/catch 拦不住，进程直接退出。
    //   路径 B 的 Utilities.GetAllEntities() 全量遍历同样带风险。
    //   两条路径在 v1.53.0 全部停用，改读 pawn 上的托管属性（ViewOffset / EyeAngles），
    //   全程不涉及任何实体 handle 解引用。
    private CBaseEntity ResolveVm(CCSPlayerPawn pawn)
    {
        _vmErr = "disabled_v1530";
        return null;
    }

    private static CBaseEntity HandleToEntity(object h)
    {
        if (h == null) return null;
        if (h is CBaseEntity e1) return e1;
        try
        {
            var pi = h.GetType().GetProperty("Value");
            if (pi != null) return pi.GetValue(h) as CBaseEntity;
        }
        catch { }
        return null;
    }

    private void ProbeVmMotion()
    {
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null || pawn.Handle == IntPtr.Zero) { _vmErr = "nopawn"; _vmHasLast = false; return; }

            if (!_vmProbeInit)
            {
                _vmProbeInit = true;
                var t = pawn.GetType();
                _vmPiVo = t.GetProperty("ViewOffset");
                _vmPiEa = t.GetProperty("EyeAngles");
                _vmCls = "prop/" + (_vmPiVo != null ? "VO" : "-") + (_vmPiEa != null ? "EA" : "-");

                // 一次性 dump：所有含 View / Eye 的属性名与类型，供定位可用取数口
                var sb = new StringBuilder();
                int cnt = 0;
                foreach (var q in t.GetProperties())
                {
                    var n = q.Name;
                    if (n.IndexOf("View", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (cnt++ >= 24) break;
                    sb.Append(n).Append(':').Append(q.PropertyType.Name).Append(' ');
                }
                LogArm("vmprobe.props " + sb.ToString());
            }

            object voObj = _vmPiVo?.GetValue(pawn);
            float ox, oy, oz;
            if (!TryVec(voObj, out ox, out oy, out oz))
            {
                // ViewOffset 不可用时，按候选顺序逐个试
                string[] cands = { "EyePosition", "LocalEyeAngles", "AbsOrigin", "AbsVelocity" };
                bool ok = false;
                foreach (var cn in cands)
                {
                    var pi = pawn.GetType().GetProperty(cn);
                    if (pi == null) continue;
                    object cv = null;
                    try { cv = pi.GetValue(pawn); } catch { continue; }
                    if (TryVec(cv, out ox, out oy, out oz))
                    {
                        _vmSrcName = cn + "/" + _vmPiVo?.PropertyType.Name;
                        ok = true;
                        break;
                    }
                }
                if (!ok)
                {
                    _vmErr = "noVec/" + (_vmPiVo == null ? "noVOprop" : (voObj == null ? "voNull" : "shape:" + voObj.GetType().Name));
                    _vmHasLast = false;
                    return;
                }
            }
            else if (_vmSrcName == "-")
            {
                _vmSrcName = "ViewOffset";
            }

            float ap = 0f, ayw = 0f, ar = 0f;
            object eaObj = _vmPiEa?.GetValue(pawn);
            float ex1, ey1, ez1;
            if (TryVec(eaObj, out ex1, out ey1, out ez1)) { ap = ex1; ayw = ey1; ar = ez1; }
            if (_vmErr.Length != 0) _vmErr = "";

            if (!_vmHasLast)
            {
                _vmHasLast = true;
                _voX = ox; _voY = oy; _voZ = oz; _vaP = ap; _vaYw = ayw; _vaR = ar;
                _voMinX = _voMaxX = ox; _voMinY = _voMaxY = oy; _voMinZ = _voMaxZ = oz;
                _vaMinP = _vaMaxP = ap; _vaMinYw = _vaMaxYw = ayw; _vaMinR = _vaMaxR = ar;
                return;
            }

            float dx = ox - _voX, dy = oy - _voY, dz = oz - _voZ;
            float dp = AngDiff(ap, _vaP), dyw = AngDiff(ayw, _vaYw), dr = AngDiff(ar, _vaR);
            float step = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float astep = (float)Math.Sqrt(dp * dp + dyw * dyw + dr * dr);
            if (step > _vmStepMax) _vmStepMax = step;
            if (astep > _vmAngStepMax) _vmAngStepMax = astep;

            if (ox < _voMinX) _voMinX = ox; if (ox > _voMaxX) _voMaxX = ox;
            if (oy < _voMinY) _voMinY = oy; if (oy > _voMaxY) _voMaxY = oy;
            if (oz < _voMinZ) _voMinZ = oz; if (oz > _voMaxZ) _voMaxZ = oz;
            if (ap < _vaMinP) _vaMinP = ap; if (ap > _vaMaxP) _vaMaxP = ap;
            if (ayw < _vaMinYw) _vaMinYw = ayw; if (ayw > _vaMaxYw) _vaMaxYw = ayw;
            if (ar < _vaMinR) _vaMinR = ar; if (ar > _vaMaxR) _vaMaxR = ar;

            _vmSamples++;
            _voX = ox; _voY = oy; _voZ = oz; _vaP = ap; _vaYw = ayw; _vaR = ar;
        }
        catch (Exception ex) { _vmErr = ex.GetType().Name; _vmHasLast = false; }
        ProbeVmOffset();
    }

    // v1.54.0：直接采 CCSPlayerPawn 上的 ViewmodelOffsetX/Y/Z（手臂/视图模型偏移），
    //   以及 ViewmodelFOV，判断手臂晃动是否由这组数值驱动。
    private void ProbeVmOffset()
    {
        try
        {
            var p = PickLocal();
            var pawn = p?.PlayerPawn?.Value;
            if (pawn == null || pawn.Handle == IntPtr.Zero) { _vfErr = "nopawn"; return; }

            if (!_vmOffInit)
            {
                _vmOffInit = true;
                var t = pawn.GetType();
                foreach (var q in t.GetProperties())
                {
                    var n = q.Name;
                    if (n == "ViewmodelOffsetX") _vmGx = q.GetGetMethod();
                    else if (n == "ViewmodelOffsetY") _vmGy = q.GetGetMethod();
                    else if (n == "ViewmodelOffsetZ") _vmGz = q.GetGetMethod();
                    else if (n == "ViewmodelFOV") _vmGfov = q.GetGetMethod();
                }
                _vfErr = "gx=" + (_vmGx != null ? 1 : 0) + "gy=" + (_vmGy != null ? 1 : 0)
                       + "gz=" + (_vmGz != null ? 1 : 0) + "gf=" + (_vmGfov != null ? 1 : 0);
            }

            float x, y, z, f;
            bool okx = TryGetRefFloat(pawn, _vmGx, out x);
            bool oky = TryGetRefFloat(pawn, _vmGy, out y);
            bool okz = TryGetRefFloat(pawn, _vmGz, out z);
            TryGetRefFloat(pawn, _vmGfov, out f);
            if (!okx && !oky && !okz) { _vfErr = "allFail"; return; }

            if (_vfSamples == 0)
            {
                _vfX = x; _vfY = y; _vfZ = z; _vfFov = f;
                _vfMinX = _vfMaxX = x; _vfMinY = _vfMaxY = y; _vfMinZ = _vfMaxZ = z;
                _vfMinFov = _vfMaxFov = f;
                _vfSamples = 1;
                _vfErr = "";
                return;
            }

            float dx = x - _vfX, dy = y - _vfY, dz = z - _vfZ;
            float st = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (st > _vfStepMax) _vfStepMax = st;
            if (x < _vfMinX) _vfMinX = x; if (x > _vfMaxX) _vfMaxX = x;
            if (y < _vfMinY) _vfMinY = y; if (y > _vfMaxY) _vfMaxY = y;
            if (z < _vfMinZ) _vfMinZ = z; if (z > _vfMaxZ) _vfMaxZ = z;
            if (f < _vfMinFov) _vfMinFov = f; if (f > _vfMaxFov) _vfMaxFov = f;

            _vfSamples++;
            _vfX = x; _vfY = y; _vfZ = z; _vfFov = f;
            if (_vfErr == "allFail") _vfErr = "";

            DiagVmHandle(pawn);
            ProbeVmEntity(pawn);
        }
        catch (Exception ex) { _vfErr = "ex:" + ex.GetType().Name; }
    }

    // 只读诊断：打印 ViewModelServices 上 m_hViewModel 的原始 4 字节值与索引，不做任何解引用
    private void DiagVmHandle(CCSPlayerPawn pawn)
    {
        try
        {
            if (!_vmHInit)
            {
                _vmHInit = true;
                try { _vmHOff1 = Schema.GetSchemaOffset("CPlayer_ViewModelServices", "m_hViewModel"); } catch { }
                try { _vmHOff2 = Schema.GetSchemaOffset("CCSPlayer_ViewModelServices", "m_hViewModel"); } catch { }
            }

            var svc = pawn.ViewModelServices;
            if (svc == null) { _vmHDiag = "svcNull"; return; }
            if (svc.Handle == IntPtr.Zero) { _vmHDiag = "svcHandleNull"; return; }

            var sb = new StringBuilder();
            sb.Append("cls=").Append(svc.GetType().Name);
            int[] offs = { _vmHOff1, _vmHOff2 };
            foreach (var off in offs)
            {
                if (off <= 0) continue;
                int r0 = 0, r1 = 0;
                try { r0 = Marshal.ReadInt32(svc.Handle, off); } catch { }
                try { r1 = Marshal.ReadInt32(svc.Handle, off + 4); } catch { }
                sb.Append(" off").Append(off).Append(':')
                  .Append(r0).Append("(idx").Append(r0 & 0x3FFF).Append(")/")
                  .Append(r1).Append("(idx").Append(r1 & 0x3FFF).Append(')');
            }
            if (_vmHOff1 <= 0 && _vmHOff2 <= 0) sb.Append(" noOff");
            _vmHDiag = sb.ToString();
            if (_vmHDiag.Length > 220) _vmHDiag = _vmHDiag.Substring(0, 220);
        }
        catch (Exception ex) { _vmHDiag = "ex:" + ex.GetType().Name; }
    }

    // v1.56.0：viewmodel 实体逐帧采样（只读）
    //   路径：pawn.ViewModelServices(CCSPlayer_ViewModelServices).ViewModel → Span<CHandle<CBaseViewModel>>
    //   —— CSS 已对外暴露该属性（TryReadIgnoreOffsetAcc 一直在用），
    //   不再依赖 Schema.GetSchemaOffset 猜偏移，也不再手工 new CHandle(raw)。
    private void ProbeVmEntity(CCSPlayerPawn pawn)
    {
        string tg = "t0";
        try
        {
            tg = "gets";
            var svcBase = pawn.ViewModelServices;
            if (svcBase == null) { _vmeErr = "svcNull"; return; }

            tg = "handle";
            IntPtr sp = svcBase.Handle;
            _vmeSP = sp.ToInt64();
            if (sp == IntPtr.Zero) { _vmeErr = "spZero/" + svcBase.GetType().Name; return; }

            // 只读快照：列出 svc 上形如 CHandle 的 4 字节值（不解引用），仅用于定位真实 m_hViewModel 偏移
            tg = "dump";
            if (_vmeDump.Length == 0) _vmeDump = DumpSvcHandles(sp);

            // 实体枚举路径：完全不依赖 schema 类名、不做任何 CHandle 解引用
            tg = "find";
            CCSGOViewModel vm = null;
            if (_vmeEntIdx > 0) { try { vm = Utilities.GetEntityFromIndex<CCSGOViewModel>(_vmeEntIdx); } catch { vm = null; } }
            if (vm != null && !VmOwnedBy(vm, pawn))
            {
                // 缓存索引被换枪/复用换掉：按 owner 重找，不做 fallback 以免每帧全量枚举
                var byOwner = FindLocalViewModel(pawn, false);
                vm = byOwner;
            }
            if (vm == null || !vm.IsValid)
            {
                _vmeEntIdx = -1;
                vm = FindLocalViewModel(pawn, true);
                if (vm == null) { _vmeErr = "noVmEnt"; return; }
            }
            _vmeEntIdx = (int)vm.Index;
            _vmeRaw = _vmeEntIdx;
            _vmeValid = vm.IsValid ? 1 : 0;

            tg = "idx";
            if (!_vmeInit) { _vmeInit = true; _vmeCls = vm.GetType().Name; }
            _vmeIdx = (int)vm.Index;

            tg = "vec";
            float ox, oy, oz, ap, ayw, ar;
            if (!TryVec(vm.AbsOrigin, out ox, out oy, out oz)) { _vmeErr = "noOrigin"; return; }
            if (!TryVec(vm.AbsRotation, out ap, out ayw, out ar)) { _vmeErr = "noRot"; return; }

            _vmeOX = ox; _vmeOY = oy; _vmeOZ = oz; _vmeOP = ap; _vmeOYw = ayw; _vmeOR = ar;

            if (!_vmeHasLast)
            {
                _vmeHasLast = true;
                _vmeLX = ox; _vmeLY = oy; _vmeLZ = oz; _vmeLP = ap; _vmeLYw = ayw; _vmeLR = ar;
                _vmeMinX = _vmeMaxX = ox; _vmeMinY = _vmeMaxY = oy; _vmeMinZ = _vmeMaxZ = oz;
                _vmeMinP = _vmeMaxP = ap; _vmeMinYw = _vmeMaxYw = ayw; _vmeMinR = _vmeMaxR = ar;
            }
            else
            {
                float dx = ox - _vmeLX, dy = oy - _vmeLY, dz = oz - _vmeLZ;
                float dp = AngDiff(ap, _vmeLP), dyw = AngDiff(ayw, _vmeLYw), dr = AngDiff(ar, _vmeLR);
                float st = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                float ast = (float)Math.Sqrt(dp * dp + dyw * dyw + dr * dr);
                if (st > _vmeStepMax) _vmeStepMax = st;
                if (ast > _vmeRotStepMax) _vmeRotStepMax = ast;

                if (ox < _vmeMinX) _vmeMinX = ox; if (ox > _vmeMaxX) _vmeMaxX = ox;
                if (oy < _vmeMinY) _vmeMinY = oy; if (oy > _vmeMaxY) _vmeMaxY = oy;
                if (oz < _vmeMinZ) _vmeMinZ = oz; if (oz > _vmeMaxZ) _vmeMaxZ = oz;
                if (ap < _vmeMinP) _vmeMinP = ap; if (ap > _vmeMaxP) _vmeMaxP = ap;
                if (ayw < _vmeMinYw) _vmeMinYw = ayw; if (ayw > _vmeMaxYw) _vmeMaxYw = ayw;
                if (ar < _vmeMinR) _vmeMinR = ar; if (ar > _vmeMaxR) _vmeMaxR = ar;

                _vmeLX = ox; _vmeLY = oy; _vmeLZ = oz; _vmeLP = ap; _vmeLYw = ayw; _vmeLR = ar;
            }

            var b = vm as CBaseViewModel;
            if (b != null)
            {
                try
                {
                    int par = Convert.ToInt32(b.AnimationParity);
                    int lay = Convert.ToInt32(b.OldLayer);
                    int seq = Convert.ToInt32(b.OldLayerSequence);
                    if (_vmeLPar == -999) { _vmeLPar = par; _vmeLLay = lay; _vmeLSeq = seq; }
                    else
                    {
                        if (par != _vmeLPar) { _vmeDPar++; _vmeLPar = par; }
                        if (lay != _vmeLLay) { _vmeDLay++; _vmeLLay = lay; }
                        if (seq != _vmeLSeq) { _vmeDSeq++; _vmeLSeq = seq; }
                    }
                }
                catch { }
                try { var g = b as CCSGOViewModel; if (g != null) _vmeIgnore = g.ShouldIgnoreOffsetAndAccuracy; } catch { }
            }

            _vmeSamples++;
            _vmeErr = "";
        }
        catch (Exception ex) { _vmeErr = "ex@" + tg + ":" + ex.GetType().Name; }
    }

    // v1.56.3：只读列出 svc 对象上形如 CHandle 的 4 字节（idx 低 14 位 / serial 高位），全不解引用
    private static string DumpSvcHandles(IntPtr sp)
    {
        var sb = new StringBuilder();
        try
        {
            for (int off = 0; off <= 0x80; off += 4)
            {
                int raw = 0;
                try { raw = Marshal.ReadInt32(sp, off); } catch { break; }
                uint u = (uint)raw;
                int idx = (int)(u & 0x3FFF);
                int ser = (int)((u >> 14) & 0x3FF);
                if (idx > 0 && ser > 0)
                {
                    sb.Append("0x").Append(off.ToString("X2")).Append("=")
                      .Append("0x").Append(u.ToString("X")).Append("(i").Append(idx).Append(",s").Append(ser).Append(") ");
                }
            }
        }
        catch { }
        string s = sb.ToString().TrimEnd();
        return s.Length == 0 ? "none" : s;
    }

    // v1.56.3：枚举实体列表定位本地玩家自己的 viewmodel 实体（OwnerEntity == pawn，allowFallback 时退路取首个）
    private static bool VmOwnedBy(CCSGOViewModel vm, CCSPlayerPawn pawn)
    {
        try
        {
            var oh = vm.OwnerEntity;
            if (oh == null) return false;
            var owner = oh.Value;
            return owner != null && owner.Handle == pawn.Handle;
        }
        catch { return false; }
    }

    private CCSGOViewModel FindLocalViewModel(CCSPlayerPawn pawn, bool allowFallback)
    {
        CCSGOViewModel fallback = null;
        try
        {
            foreach (var ent in Utilities.GetAllEntities())
            {
                var vm = ent as CCSGOViewModel;
                if (vm == null) continue;
                bool ok = false;
                try { ok = vm.IsValid; } catch { }
                if (!ok) continue;

                if (VmOwnedBy(vm, pawn)) return vm;
                if (fallback == null) fallback = vm;
            }
        }
        catch { }
        return allowFallback ? fallback : null;
    }

    // v1.56.0：每 beat 输出一个窗口的 viewmodel 实体统计，并复位窗口
    private void LogVmEntityBeat()
    {
        if (_vmeSamples == 0 && _vmeErr.Length == 0) return;

        string s = "vme | cls=" + _vmeCls + " idx=" + _vmeIdx + " sp=0x" + _vmeSP.ToString("X") + " raw=" + _vmeRaw + " vld=" + _vmeValid
            + " oΔ=" + (_vmeMaxX - _vmeMinX).ToString("0.0000") + "/" + (_vmeMaxY - _vmeMinY).ToString("0.0000") + "/" + (_vmeMaxZ - _vmeMinZ).ToString("0.0000")
            + " stp=" + _vmeStepMax.ToString("0.0000")
            + " rΔ=" + (_vmeMaxP - _vmeMinP).ToString("0.000") + "/" + (_vmeMaxYw - _vmeMinYw).ToString("0.000") + "/" + (_vmeMaxR - _vmeMinR).ToString("0.000")
            + " rstp=" + _vmeRotStepMax.ToString("0.000")
            + " now=" + _vmeOX.ToString("0.000") + "," + _vmeOY.ToString("0.000") + "," + _vmeOZ.ToString("0.000")
            + " rot=" + _vmeOP.ToString("0.000") + "," + _vmeOYw.ToString("0.000") + "," + _vmeOR.ToString("0.000")
            + " aniΔ=" + _vmeDPar + "/" + _vmeDLay + "/" + _vmeDSeq
            + " lay=" + _vmeLLay + " seq=" + _vmeLSeq + " ign=" + (_vmeIgnore ? 1 : 0)
            + " ent=" + _vmeEntIdx
            + " n=" + _vmeSamples + (_vmeErr.Length != 0 ? " err=" + _vmeErr : "")
            + (_vmeDump.Length != 0 ? " dump=" + _vmeDump : "");
        _vmeDump = "";
        LogArm(s);

        _vmeMinX = _vmeMaxX = _vmeLX; _vmeMinY = _vmeMaxY = _vmeLY; _vmeMinZ = _vmeMaxZ = _vmeLZ;
        _vmeMinP = _vmeMaxP = _vmeLP; _vmeMinYw = _vmeMaxYw = _vmeLYw; _vmeMinR = _vmeMaxR = _vmeLR;
        _vmeStepMax = 0f; _vmeRotStepMax = 0f;
        _vmeDPar = 0; _vmeDLay = 0; _vmeDSeq = 0;
        _vmeSamples = 0;
    }

    private string VmProbeReport()
    {
        string s = " vmO=" + (_voMaxX - _voMinX).ToString("0.0000") + "/" + (_voMaxY - _voMinY).ToString("0.0000") + "/" + (_voMaxZ - _voMinZ).ToString("0.0000")
            + " vmA=" + (_vaMaxP - _vaMinP).ToString("0.000") + "/" + (_vaMaxYw - _vaMinYw).ToString("0.000") + "/" + (_vaMaxR - _vaMinR).ToString("0.000")
            + " vmSm=" + _vmStepMax.ToString("0.0000") + " vmSa=" + _vmAngStepMax.ToString("0.000")
            + " vmN=" + _vmSamples + " vmC=" + _vmCls + "/" + _vmSrcName + "/" + _vmArrN
            + " vfO=" + (_vfMaxX - _vfMinX).ToString("0.0000") + "/" + (_vfMaxY - _vfMinY).ToString("0.0000") + "/" + (_vfMaxZ - _vfMinZ).ToString("0.0000")
            + " vfV=" + _vfX.ToString("0.0000") + "/" + _vfY.ToString("0.0000") + "/" + _vfZ.ToString("0.0000")
            + " vfSm=" + _vfStepMax.ToString("0.0000") + " vfFov=" + _vfMinFov.ToString("0.00") + "~" + _vfMaxFov.ToString("0.00")
            + " vfN=" + _vfSamples + (_vfErr.Length != 0 ? " vfErr=" + _vfErr : "")
            + " hm=" + _vmHDiag + (_vmErr.Length != 0 ? " vmErr=" + _vmErr : "");
        _voMinX = _voMaxX = _voX; _voMinY = _voMaxY = _voY; _voMinZ = _voMaxZ = _voZ;
        _vaMinP = _vaMaxP = _vaP; _vaMinYw = _vaMaxYw = _vaYw; _vaMinR = _vaMaxR = _vaR;
        _vmStepMax = 0f; _vmAngStepMax = 0f; _vmSamples = 0;
        _vfMinX = _vfMaxX = _vfX; _vfMinY = _vfMaxY = _vfY; _vfMinZ = _vfMaxZ = _vfZ;
        _vfMinFov = _vfMaxFov = _vfFov; _vfStepMax = 0f; _vfSamples = 1;
        return s;
    }

    private void BeatArm()
    {
        if (!_armReady) return;
        double now = _sw.Elapsed.TotalSeconds;
        if (now - _lastBeatArm < 3.0) return;
        _lastBeatArm = now;

        double dt = now - _rateStamp;
        if (dt > 0.2)
        {
            _tickRate = (int)Math.Round(_tickAcc / dt);
            int ec = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_EXIT);
            int nc = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_ENTRY);
            int sc = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_SNAP);
            int rc = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_ROLL);
            _exitHz = (int)Math.Round((ec - _lastExitCnt) / dt);
            _entryHz = (int)Math.Round((nc - _lastEntryCnt) / dt);
            _snapHz = (int)Math.Round((sc - _lastSnapCnt) / dt);
            _rollHz = (int)Math.Round((rc - _lastRollCnt) / dt);
            _lastExitCnt = ec; _lastEntryCnt = nc; _lastSnapCnt = sc; _lastRollCnt = rc;
            _tickAcc = 0;
            _rateStamp = now;
        }

        // v1.39.2：bobacc 跳板命中回读 —— 累计「进入次数 / 真正跳过次数」。
        //   两者相等且随 exitHz 同步增长 → 2 档确实每帧都跳过了累加段；
        //   若 skip 远小于进入次数 → state 读到的不等于 2（共享区没刷进来），问题不在 cave。
        int bac = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_BOBACC);
        int bas = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_BOBACC_SKIP);
        int wa2 = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_WA2);
        int waAll = _shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_CNT_WA_ALL);
        int snapState = _shared == IntPtr.Zero ? -999 : Marshal.ReadInt32(_shared, SH_STATE_SNAP);
        int shState = _shared == IntPtr.Zero ? -999 : Marshal.ReadInt32(_shared, SH_STATE);
        LogArm("beat | cl_bob=" + ReadCvar(CI_BOB).ToString("0.###") + " state=" + _state + " eff=" + _effState + " dl=abs" + " tickHz=" + _tickRate
            + " entryHz=" + _entryHz + " snapHz=" + _snapHz + " exitHz=" + _exitHz + " rollHz=" + _rollHz
            + " bobacc=" + bac + "/" + bas + " wa=" + waAll + "/" + wa2 + " spdN=" + (_shared == IntPtr.Zero ? 0 : Marshal.ReadInt32(_shared, SH_SPD_N)) + " shState=" + shState + " snap=" + snapState
            + " spd=" + _spd.ToString("0.0") + " cyc=" + _lastCycle.ToString("0.###") + " ms=" + _lastMaxSpeed.ToString("0")
            + " vert=" + _bobAmp.ToString("0.000") + " fb=" + _bobFb.ToString("0.000") + " lat=" + _bobLr.ToString("0.000")
            + " pit=" + _bobPitch.ToString("0.000") + " yaw=" + _bobYaw.ToString("0.000")
            + " engR=" + _phaseRate.ToString("0.000") + " engBk=" + _engBacks + " engFr=" + (_engStill > 0.0 ? 1 : 0) + " src=" + (_useNative ? "offline" : "server") + " nspd=" + _nSpd.ToString("0.####")
            + " nvm=" + (_nVm ? 1 : 0) + " nseq=" + _nSeq
            + " params=手动(src=" + (_pSrcBg ? "BG2" : "cvar") + ")" + " v=" + _pVert.ToString("0.###") + " l=" + _pLat.ToString("0.###")
            + " low=" + _pLower.ToString("0.###") + " alive=" + (_inAlive ? 1 : 0) + " vm=" + (_inHasWeapon ? 1 : 0)
            + " air=" + (_inAir ? 1 : 0) + " maxspd=" + ((_state == 2) ? BG2_MAXSPEED : _inMaxSpeed).ToString("0")
            + " base=" + F(SH_BASE_LR).ToString("0.00") + "/" + F(SH_BASE_FB).ToString("0.00") + "/"
            + F(SH_BASE_UD).ToString("0.00") + "/" + F(SH_BASE_LOW).ToString("0.00") + "/"
            + F(SH_BASE_ANG_Y).ToString("0.00") + "/" + F(SH_BASE_ANG_Z).ToString("0.00")
            + " cur=" + (_engPtrRva < 0 ? "wall" : (_engClock == IntPtr.Zero ? "obj-null"
                        : ReadF32(_engClock + ENG_CLOCK_OFF).ToString("0.000")))
            + " curJ=" + _engJumps + " curHold=" + _engStalls
            + " sel=" + _selTag + VmProbeReport());
        LogVmEntityBeat();
    }

    // ================================================================ v1.25.0 探针安装
    private sealed class ProbeSite
    {
        public string Name;
        public int Rva;
        public byte[] Stolen;
        public bool DumpRdi;
        public int BiasKind;      // v1.25.1：1 = 读 xmm0←[rdi+0x0c] 处加 dX；2 = 读 xmm1←[rdi+0x10] 处加 dY；0 = 不偏置
        public float DeltaVal;    // 该站点的叠加量
    }

    private static readonly ProbeSite[] ProbeSites = new ProbeSite[]
    {
        new ProbeSite { Name = "overlay.frame", Rva = 0xCA32C0, DumpRdi = false,
            Stolen = new byte[] { 0x4C, 0x8B, 0xDC, 0x49, 0x89, 0x5B, 0x10 } },
        new ProbeSite { Name = "legacy.queue", Rva = 0xC931F0, DumpRdi = false,
            Stolen = new byte[] { 0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x56 } },
        new ProbeSite { Name = "scope.draw", Rva = 0xC93886, DumpRdi = true,
            Stolen = new byte[] { 0x4C, 0x8D, 0x63, 0x10, 0x80, 0x7F, 0x04, 0x00 } },
        // v1.25.1 关键：这两个 movss 是该函数里唯一读取抖动 float 的地方（全函数无写回），
        // 改寄存器副本即可完成静态偏置，不污染游戏堆、不会逐帧累积
        new ProbeSite { Name = "bias.X", Rva = 0xC939E3, BiasKind = 1, DeltaVal = SCOPE_PROBE_DELTA_X,
            Stolen = new byte[] { 0xF3, 0x0F, 0x10, 0x47, 0x0C } },   // movss xmm0,[rdi+0x0c]
        new ProbeSite { Name = "bias.Y", Rva = 0xC939ED, BiasKind = 2, DeltaVal = SCOPE_PROBE_DELTA_Y,
            Stolen = new byte[] { 0xF3, 0x0F, 0x10, 0x4F, 0x10 } },   // movss xmm1,[rdi+0x10]
        new ProbeSite { Name = "stencil.draw", Rva = 0xC9F389, DumpRdi = false,
            Stolen = new byte[] { 0x48, 0x8D, 0x4D, 0xFF, 0x44, 0x89, 0x6D, 0xF7 } },
        new ProbeSite { Name = "dead.CB1370", Rva = 0xCB1370, DumpRdi = false,
            Stolen = new byte[] { 0x4C, 0x8B, 0xDC, 0x49, 0x89, 0x5B, 0x10 } },
    };

    private void InstallProbes()
    {
        if (_base == IntPtr.Zero) { LogScope("探针：_base 为空，跳过"); return; }

        _pShared = Marshal.AllocHGlobal(PSH_SIZE);
        for (int i = 0; i < PSH_SIZE; i++) Marshal.WriteByte(_pShared, i, 0);
        _pCave = new IntPtr[ProbeSites.Length];

        for (int k = 0; k < ProbeSites.Length; k++)
        {
            ProbeSite ps = ProbeSites[k];
            if (ps.BiasKind != 0) { InstallBiasProbe(k, ps); continue; }
            IntPtr at = _base + ps.Rva;
            byte[] orig = ReadBytes(ps.Rva, ps.Stolen.Length);
            bool same = orig.Length == ps.Stolen.Length;
            if (same) for (int i = 0; i < ps.Stolen.Length; i++) if (orig[i] != ps.Stolen[i]) same = false;
            if (!same)
            {
                LogScope("探针 " + ps.Name + " 跳过：原字节不匹配（实际 " + Hex(orig) + "）");
                continue;
            }

            List<byte> code = new List<byte>();
            if (ps.DumpRdi) { code.Add(0x50); code.Add(0x51); code.Add(0x52); code.Add(0x53); } // push rax rcx rdx rbx
            code.Add(0x48); code.Add(0xB8);
            code.AddRange(BitConverter.GetBytes(_pShared.ToInt64()));         // mov rax, shared
            code.Add(0xFF); code.Add(0x80);
            code.AddRange(BitConverter.GetBytes(k * 4));                     // inc dword [rax + k*4]
            if (ps.DumpRdi)
            {
                for (int j = 0; j < 10; j++)                                   // rdi+0x00..0x24 → 0x20 起
                {
                    code.Add(0x8B); code.Add(0x4F); code.Add((byte)(j * 4));        // mov ecx,[rdi+off]
                    code.Add(0x89); code.Add(0x88);
                    code.AddRange(BitConverter.GetBytes(PSH_RDI + j * 4));          // mov [rax+PSH_RDI+off], ecx
                }
                for (int j = 0; j < 10; j++)                                   // rbx+0x00..0x24 → 0x50 起
                {
                    code.Add(0x8B); code.Add(0x4B); code.Add((byte)(j * 4));        // mov ecx,[rbx+off]
                    code.Add(0x89); code.Add(0x88);
                    code.AddRange(BitConverter.GetBytes(PSH_RBX + j * 4));          // mov [rax+PSH_RBX+off], ecx
                }
            }
            if (ps.DumpRdi) { code.Add(0x5B); code.Add(0x5A); code.Add(0x59); code.Add(0x58); } // pop rbx rdx rcx rax
            code.AddRange(ps.Stolen);

            IntPtr cave = AllocNear(_base, 0x1000);
            if (cave == IntPtr.Zero) { LogScope("探针 " + ps.Name + " 跳过：cave 分配失败"); continue; }
            long back = _base.ToInt64() + ps.Rva + ps.Stolen.Length;
            long relBack = back - (cave.ToInt64() + code.Count + 5);
            if (relBack < int.MinValue || relBack > int.MaxValue)
            { LogScope("探针 " + ps.Name + " 跳过：回流 rel32 不可达"); continue; }
            code.Add(0xE9);
            code.AddRange(BitConverter.GetBytes((int)relBack));

            byte[] blob = code.ToArray();
            Marshal.Copy(blob, 0, cave, blob.Length);
            FlushInstructionCache(GetCurrentProcess(), cave, (UIntPtr)blob.Length);

            long relJump = cave.ToInt64() - (at.ToInt64() + 5);
            if (relJump < int.MinValue || relJump > int.MaxValue)
            { LogScope("探针 " + ps.Name + " 跳过：跳转 rel32 不可达"); continue; }
            byte[] patched = new byte[ps.Stolen.Length];
            patched[0] = 0xE9;
            BitConverter.GetBytes((int)relJump).CopyTo(patched, 1);
            for (int i = 5; i < patched.Length; i++) patched[i] = 0x90;

            if (!WriteBytesAt(at, patched))
            { LogScope("探针 " + ps.Name + " 跳过：写入失败"); continue; }

            _pCave[k] = cave;
            LogScope("探针 " + ps.Name + " 已装 @0x" + ps.Rva.ToString("X") + " cave=0x" + cave.ToInt64().ToString("X")
                + "（" + blob.Length + "B）原字节 " + Hex(ps.Stolen) + " 保护 " + ps.Stolen.Length + "B");
        }

        _probesReady = true;
        LogScope("探针全部就绪：拿狙开镜跑 3~5 秒，然后看 beat.probe 行哪个 Hz>0");
    }

    // v1.25.1 偏置站点：原生 movss 取抖动 float → 若 flag=1 则 addss 常量 → 回流
    // 只动 xmm 寄存器副本，不写游戏内存，天然不累积；cave 自带数据区（flag/Δ 内联在 cave 里，RIP 相对访问）
    private void InstallBiasProbe(int k, ProbeSite ps)
    {
        IntPtr at = _base + ps.Rva;
        byte[] orig = ReadBytes(ps.Rva, ps.Stolen.Length);
        bool same = orig.Length == ps.Stolen.Length;
        if (same) for (int i = 0; i < ps.Stolen.Length; i++) if (orig[i] != ps.Stolen[i]) same = false;
        if (!same)
        {
            LogScope("偏置站 " + ps.Name + " 跳过：原字节不匹配（实际 " + Hex(orig) + "）");
            return;
        }

        List<byte> code = new List<byte>();
        // v1.25.2：真实命中计数 —— 证明本 cave 每帧确实被执行（写探针共享区 [k*4]）
        if (_pShared != IntPtr.Zero)
        {
            code.Add(0x50);                                              // push rax
            code.Add(0x48); code.Add(0xB8);                              // mov rax, imm64
            code.AddRange(BitConverter.GetBytes(_pShared.ToInt64()));
            code.Add(0xFF); code.Add(0x80);                              // inc dword [rax+disp32]
            code.AddRange(BitConverter.GetBytes(k * 4));
            code.Add(0x58);                                              // pop rax
        }
        code.AddRange(ps.Stolen);                                        // movss xmm?,[rdi+off]（0 档直通路径）
        int cmpDisp = code.Count + 2;
        code.Add(0x80); code.Add(0x3D);                                  // cmp byte [rip+d],0
        code.AddRange(new byte[4]); code.Add(0x00);
        int jePos = code.Count;
        code.Add(0x74); code.Add(0x00);                                  // je 直通
        // v1.29.0：闸门开 → 覆盖式替换，xmm 直接取 cave 数据区的 VAL（插件单源 legacy 值）
        int repDisp = code.Count + 4;
        code.Add(0xF3); code.Add(0x0F); code.Add(0x10);                  // movss xmm?, [rip+d]
        code.Add(ps.BiasKind == 1 ? (byte)0x05 : (byte)0x0D);            // 05 = xmm0 / 0D = xmm1
        code.AddRange(new byte[4]);
        int jmpOnPos = code.Count;
        code.Add(0xE9);                                                  // jmp back（覆盖分支）
        code.AddRange(new byte[4]);
        int skipOff = code.Count;                                        // 直通落点
        int jmpOffPos = code.Count;
        code.Add(0xE9);                                                  // jmp back（直通分支）
        code.AddRange(new byte[4]);
        while (code.Count < CAVE_BIAS_FLAG) code.Add(0xCC);              // 补齐到数据区

        code[jePos + 1] = (byte)(skipOff - (jePos + 2));                 // je 落点 = 直通分支起点
        {
            byte[] d1 = BitConverter.GetBytes(CAVE_BIAS_FLAG - (cmpDisp + 5));  // cmp 指令末尾 = disp 起点+4+imm8
            byte[] d2 = BitConverter.GetBytes(CAVE_BIAS_VAL - (repDisp + 4));
            for (int m = 0; m < 4; m++) { code[cmpDisp + m] = d1[m]; code[repDisp + m] = d2[m]; }
        }

        IntPtr cave = AllocNear(_base, 0x1000);
        if (cave == IntPtr.Zero) { LogScope("偏置站 " + ps.Name + " 跳过：cave 分配失败"); return; }

        long back = _base.ToInt64() + ps.Rva + ps.Stolen.Length;
        long relBack = back - (cave.ToInt64() + jmpOnPos + 5);
        long relBack2 = back - (cave.ToInt64() + jmpOffPos + 5);
        if (relBack < int.MinValue || relBack > int.MaxValue ||
            relBack2 < int.MinValue || relBack2 > int.MaxValue)
        { LogScope("偏置站 " + ps.Name + " 跳过：回流 rel32 不可达"); VirtualFree(cave, UIntPtr.Zero, MEM_RELEASE); return; }
        byte[] rb = BitConverter.GetBytes((int)relBack);
        byte[] rb2 = BitConverter.GetBytes((int)relBack2);
        // 注意：jmpOnPos / jmpOffPos 指的是 E9 操作码位置，rel32 从下一字节开始（+1），
        // 而 rel 基准是「指令末尾」= 操作码位置 + 5。
        for (int m = 0; m < 4; m++) { code[jmpOnPos + 1 + m] = rb[m]; code[jmpOffPos + 1 + m] = rb2[m]; }

        byte[] blob = code.ToArray();
        Marshal.Copy(blob, 0, cave, blob.Length);
        Marshal.WriteByte(cave, CAVE_BIAS_FLAG, 0);                      // 默认关
        Marshal.WriteInt32(cave, CAVE_BIAS_VAL, 0);                      // v1.29.0：覆盖值初值 0，闸门默认关
        FlushInstructionCache(GetCurrentProcess(), cave, (UIntPtr)blob.Length);

        long relJump = cave.ToInt64() - (at.ToInt64() + 5);
        if (relJump < int.MinValue || relJump > int.MaxValue)
        { LogScope("偏置站 " + ps.Name + " 跳过：跳转 rel32 不可达"); VirtualFree(cave, UIntPtr.Zero, MEM_RELEASE); return; }

        byte[] patched = new byte[5];
        patched[0] = 0xE9;
        BitConverter.GetBytes((int)relJump).CopyTo(patched, 1);
        if (!WriteBytesAt(at, patched))
        { LogScope("偏置站 " + ps.Name + " 跳过：写入失败"); VirtualFree(cave, UIntPtr.Zero, MEM_RELEASE); return; }

        _pCave[k] = cave;
        Marshal.WriteByte(cave, CAVE_BIAS_FLAG, 0);
        LogScope("偏置站 " + ps.Name + " 已装 @0x" + ps.Rva.ToString("X") + " cave=0x" + cave.ToInt64().ToString("X")
            + "（" + blob.Length + "B，覆盖式：闸门开=movss 取 cave 值，闸门关=原生 movss 直通）");
    }

    private int ProbeIndex(string name)
    {
        for (int i = 0; i < ProbeSites.Length; i++) if (ProbeSites[i].Name == name) return i;
        return -1;
    }

    /// <summary>v1.29.0：只开关闸门；具体覆盖值由每帧的 SyncBiasDelta 写入。</summary>
    private void SetBiasSites(bool on)
    {
        for (int k = 0; k < ProbeSites.Length; k++)
        {
            if (ProbeSites[k].BiasKind == 0) continue;
            IntPtr c = (_pCave != null) ? _pCave[k] : IntPtr.Zero;
            if (c == IntPtr.Zero) continue;
            // 开闸前先把值写 0，避免出现一帧残值；开闸后由 SyncBiasDelta 每帧刷新
            if (on) Marshal.WriteInt32(c, CAVE_BIAS_VAL, 0);
            Marshal.WriteByte(c, CAVE_BIAS_FLAG, (byte)(on ? 1 : 0));
        }
    }

    /// <summary>v1.31.0：每帧按当前档位写覆盖值 —— 1 档 = 单源 legacy（lat→X、vert→Y，固定 ×5），0 档 = 关闸。</summary>
    private void SyncBiasDelta()
    {
        if (_pCave == null || !_probesReady) return;
        int st = _sInState;
        if (st == 0)
        {
            for (int k = 0; k < ProbeSites.Length; k++)
            {
                if (ProbeSites[k].BiasKind == 0) continue;
                IntPtr c = _pCave[k];
                if (c == IntPtr.Zero) continue;
                if (Marshal.ReadByte(c, CAVE_BIAS_FLAG) != 0) Marshal.WriteByte(c, CAVE_BIAS_FLAG, 0);
            }
            return;
        }

        float vLat = 0f, vVert = 0f;
        if (st == 1 && _bobRawOk)
        {
            // lat 本身是 aL*sin（零均值）；vert 需减掉直流分量，只取 aV*0.7*sin，避免整个框被推走
            // v1.32.0：幅度增益固定 SCOPE_GAIN=10.0（后台常量，无 cvar）
            vLat = _bobLatRaw * SCOPE_LEGACY_UNIT_LAT * SCOPE_GAIN;
            vVert = (_bobVertRaw - _bobVertDc) * SCOPE_LEGACY_UNIT_VERT * SCOPE_GAIN;
        }

        for (int k = 0; k < ProbeSites.Length; k++)
        {
            int kind = ProbeSites[k].BiasKind;
            if (kind == 0) continue;
            IntPtr c = _pCave[k];
            if (c == IntPtr.Zero) continue;
            float v = kind == 1 ? vLat : vVert;
            Marshal.WriteInt32(c, CAVE_BIAS_VAL, BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
            if (Marshal.ReadByte(c, CAVE_BIAS_FLAG) == 0) Marshal.WriteByte(c, CAVE_BIAS_FLAG, 1);
        }
    }

    private void UninstallProbes()
    {
        for (int k = 0; k < ProbeSites.Length; k++)
        {
            if (_pCave == null || _pCave[k] == IntPtr.Zero) continue;
            WriteBytesAt(_base + ProbeSites[k].Rva, ProbeSites[k].Stolen);
            LogScope("探针 " + ProbeSites[k].Name + " 已还原原生字节");
            VirtualFree(_pCave[k], UIntPtr.Zero, MEM_RELEASE);
            _pCave[k] = IntPtr.Zero;
        }
        _probesReady = false;
    }

    private bool WriteBytesAt(IntPtr at, byte[] bytes)
    {
        if (!VirtualProtect(at, (UIntPtr)16, PAGE_EXECUTE_READWRITE, out uint old))
        {
            LogScope("VirtualProtect 失败 err=" + Marshal.GetLastWin32Error());
            return false;
        }
        bool ok = true;
        try
        {
            if ((at.ToInt64() & 7L) == 0)
            {
                byte[] full = new byte[8];
                Buffer.BlockCopy(bytes, 0, full, 0, bytes.Length >= 8 ? 8 : bytes.Length);
                for (int i = bytes.Length; i < 8; i++) full[i] = Marshal.ReadByte(at, i);
                long v = BitConverter.ToInt64(full, 0);
                unsafe { Interlocked.Exchange(ref *(long*)at.ToPointer(), v); }
                FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)8);
            }
            else
            {
                Marshal.Copy(bytes, 0, at, bytes.Length);
                FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)bytes.Length);
            }
        }
        catch (Exception ex)
        {
            LogScope("探针写入异常：" + ex.GetType().Name + " " + ex.Message);
            ok = false;
        }
        VirtualProtect(at, (UIntPtr)16, old, out _);
        return ok;
    }

    private void BeatProbes(double now)
    {
        SyncBiasDelta();                       // v1.29.0：每拍兜底重申覆盖值（1 档单源 legacy / 2 档 0 / 0 档关闸）
        bool upd = (now - _sHzStamp) > 1.0;
        double dt = upd ? (now - _sHzStamp) : 0.0;

        string line = "beat.probe |";
        for (int k = 0; k < ProbeSites.Length; k++)
        {
            int c = Marshal.ReadInt32(_pShared, k * 4);
            if (upd && dt > 0)
            {
                _pHits[k] = (int)Math.Round((c - _pLastCnt[k]) / dt);
                _pLastCnt[k] = c;
            }
            line += " " + ProbeSites[k].Name + "=" + _pHits[k] + "Hz/" + c;
        }
        if (upd) _sHzStamp = now;

        int iScope = ProbeIndex("scope.draw");
        int iBx = ProbeIndex("bias.X");
        int iBy = ProbeIndex("bias.Y");
        bool scopeLive = iScope >= 0 && _pHits[iScope] > 0;
        if (scopeLive || (iBx >= 0 && _pHits[iBx] > 0) || (iBy >= 0 && _pHits[iBy] > 0))
        {
            int gate = 0; float bdx = 0f, bdy = 0f;
            if (iBx >= 0 && _pCave != null && _pCave[iBx] != IntPtr.Zero)
            { gate = Marshal.ReadByte(_pCave[iBx], CAVE_BIAS_FLAG); bdx = CaveFloat(_pCave[iBx], CAVE_BIAS_VAL); }
            if (iBy >= 0 && _pCave != null && _pCave[iBy] != IntPtr.Zero)
                bdy = CaveFloat(_pCave[iBy], CAVE_BIAS_VAL);
            // v1.29.0：日志打「覆盖模式」——mode=replace 时 dX/dY 就是真正喂给镜框的值
            line += " | bias gate=" + gate + " gain=" + SCOPE_GAIN.ToString("0.#") + " mode=" + (_sInState == 0 ? "pass" : "replace")
                + " dX=" + bdx.ToString("0.###") + " dY=" + bdy.ToString("0.###")
                + " | arm=(" + _bobLatRaw.ToString("0.###") + "," + _bobVertRaw.ToString("0.###") + ")"
                + " | rdi12=" + ProbeFloat(PSH_RDI + 0x0C) + " rdi16=" + ProbeFloat(PSH_RDI + 0x10)
                + " | rdi[0..24]=" + ProbeInts(PSH_RDI, 10)
                + " | rbx[0..24]=" + ProbeInts(PSH_RBX, 10)
                + " | rect=(" + Marshal.ReadInt32(_pShared, PSH_RBX + 0x10) + "," + Marshal.ReadInt32(_pShared, PSH_RBX + 0x14)
                + "," + Marshal.ReadInt32(_pShared, PSH_RBX + 0x18) + "," + Marshal.ReadInt32(_pShared, PSH_RBX + 0x1C) + ")";
        }
        LogScope(line);
    }

    private string ProbeInts(int off, int n)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int j = 0; j < n; j++)
        {
            if (j > 0) sb.Append(',');
            sb.Append(Marshal.ReadInt32(_pShared, off + j * 4));
        }
        return sb.ToString();
    }

    private string ProbeFloat(int off)
    {
        int v = Marshal.ReadInt32(_pShared, off);
        return BitConverter.ToSingle(BitConverter.GetBytes(v), 0).ToString("0.###");
    }

    private float CaveFloat(IntPtr cave, int off)
    {
        int v = Marshal.ReadInt32(cave, off);
        return BitConverter.ToSingle(BitConverter.GetBytes(v), 0);
    }

    private void BeatScope()
    {
        if (!_scopeReady) return;
        double now = _sw.Elapsed.TotalSeconds;
        if (now - _lastBeatScope < 3.0) return;
        _lastBeatScope = now;

        if (_probesReady && _pShared != IntPtr.Zero) { BeatProbes(now); return; }

        if (_sShared == IntPtr.Zero) { LogScope("beat | 未初始化（shared 为空）"); return; }

        int cnt = Marshal.ReadInt32(_sShared, SSH_CNT);
        if (now - _sHzStamp > 1.0)
        {
            _sWaveHz = (int)Math.Round((cnt - _sLastCnt) / (now - _sHzStamp));
            _sLastCnt = cnt; _sHzStamp = now;
        }
        int nx = Marshal.ReadInt32(_sShared, SSH_NATX), ny = Marshal.ReadInt32(_sShared, SSH_NATY);
        int ox = Marshal.ReadInt32(_sShared, 0x60), oy = Marshal.ReadInt32(_sShared, 0x64);
        int r2 = Marshal.ReadInt32(_sShared, 0x68), r3 = Marshal.ReadInt32(_sShared, 0x6c);
        LogScope("beat | cl_bob_sniper=" + _sState + " hooked=" + _sHooked + " renderHz=" + _sWaveHz
            + " spd=" + _sSpd.ToString("0.0")
            + " Δ=(" + Marshal.ReadInt32(_sShared, SSH_DX) + "," + Marshal.ReadInt32(_sShared, SSH_DY) + ")"
            + " rectIn=(" + nx + "," + ny + "," + r2 + "," + r3 + ")"
            + " rectOut=(" + ox + "," + oy + ")"
            + " | amt vert=" + ReadCvar(CI_VERT).ToString("0.###") + " lat=" + ReadCvar(CI_LAT).ToString("0.###")
            + " bobTime=" + _sBobTime.ToString("0.000"));
    }

    // ---------------------------------------------------------------- cl_bob_diag（v1.23.5 后台固定 1）
    private void DiagCheck()
    {
        const int iv = DIAG_FIXED;
        if (iv <= 0) return;
        if (iv == _lastDiag) return;
        _lastDiag = iv;
        DumpLocTable("cl_bob_diag=" + iv + "（后台固定）");
    }

    private void DumpLocTable(string tag)
    {
        Line("===== AOB 定位报告（" + tag + "）=====");
        Line("目标            命中   解析RVA     基准RVA     差值      签名");
        if (_loc == null || !_loc.TextMapped)
        {
            Line("(.text 未映射，无定位结果)");
            return;
        }
        foreach (SigResult s in _loc.All)
        {
            string rva = s.ResolvedRva > 0 ? "0x" + s.ResolvedRva.ToString("X8") : "--------  ";
            string delta = s.ResolvedRva > 0 ? AobLocator.Signed(s.ResolvedRva - s.BaselineRva) : "-";
            Line(Pad(s.Name, 15) + Pad(s.HitsText, 6) + Pad(rva, 12) + Pad("0x" + s.BaselineRva.ToString("X8"), 12)
                + Pad(delta, 10) + s.Display);
        }
        Line("通道状态：arm=" + (_armReady ? "启用" : "未启用") + " scope=" + (_scopeReady ? "启用" : "未启用")
            + "（" + N_CVARS + " 项 cvar 已就绪）");
        Line("提示：差值非 0 说明本次 client.dll 布局已变化，签名仍命中即无需处理；命中数 != 1 才需要重新提签名");
    }

    /// <summary>定位报告双写：日志 + 服务器控制台（控制台看不到也能翻 cl_bob.log）。</summary>
    private void Line(string msg)
    {
        LogLoc(msg);
        try { Server.PrintToConsole("[BobCore] " + msg); } catch { }
    }

    private static string Pad(string s, int w)
    {
        if (s == null) s = "";
        if (s.Length >= w) return s + " ";
        return s + new string(' ', w - s.Length);
    }

    // ================================================================ 持久化
    private void LoadPersisted()
    {
        for (int i = 0; i < N_CVARS; i++) _persistVal[i] = -1f;
        try
        {
            if (!File.Exists(PersistPath))
            {
                for (int i = 0; i < N_CVARS; i++) _persistVal[i] = ReadCvar(i);
                LogCfg("无持久化文件 " + PersistPath + "，沿用 cvar 当前值");
                return;
            }

            bool any = false;
            foreach (string raw in File.ReadAllLines(PersistPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '/') continue;
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                string key = line.Substring(0, sp).Trim();
                string val = line.Substring(sp + 1).Trim();
                int idx = Array.IndexOf(CvarNames, key);
                if (idx < 0)
                {
                    for (int i = 0; i < N_CVARS; i++)
                        if (string.Equals(key, CvarNames[i], StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
                if (idx < 0)
                {
                    for (int i = 0; i < RemovedCvarNames.Length; i++)
                    {
                        if (!string.Equals(key, RemovedCvarNames[i], StringComparison.OrdinalIgnoreCase)) continue;
                        LogCfg("旧配置键 " + key + " 已移除/已后台化（v1.23.5），忽略该行");
                        break;
                    }
                }
                if (idx < 0 || idx >= N_PERSIST) continue;
                float f;
                if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                SetCvar(idx, f);
                _persistVal[idx] = ReadCvar(idx);
                any = true;
                LogCfg("持久化回填 " + CvarNames[idx] + " = " + f.ToString("0.####") + "（读回 " + _persistVal[idx].ToString("0.####") + "）");
            }
            if (!any) LogCfg("持久化文件为空/无有效行，沿用 cvar 当前值");
        }
        catch (Exception ex) { LogCfg("读取持久化失败: " + ex.Message); }

        for (int i = 0; i < N_CVARS; i++) if (_persistVal[i] < 0f) _persistVal[i] = ReadCvar(i);
        _persistDirty = false;
    }

    private void PersistCheck()
    {
        bool changed = false;
        for (int i = 0; i < N_PERSIST; i++)
        {
            float v = ReadCvar(i);
            if (!(v >= 0f)) continue;
            if (Math.Abs(v - _persistVal[i]) <= 0.0001f) continue;
            _persistVal[i] = v;
            changed = true;
        }
        if (changed)
        {
            _persistDirty = true;
            _persistStamp = _sw.Elapsed.TotalSeconds;
        }
        if (!_persistDirty) return;
        if (_sw.Elapsed.TotalSeconds - _persistStamp < 1.2) return;
        SavePersist();
    }

    private void SavePersist()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# BobCore v1.23.1 自动保存：局内改 cvar 后写盘，插件加载时回填（格式与 ClBob v1.15.0 兼容）");
            for (int i = 0; i < N_PERSIST; i++)
                sb.AppendLine(CvarNames[i] + " " + _persistVal[i].ToString("0.####", CultureInfo.InvariantCulture));
            File.WriteAllText(PersistPath, sb.ToString(), new UTF8Encoding(false));
            _persistDirty = false;
            StringBuilder st = new StringBuilder();
            for (int i = 0; i < N_PERSIST; i++)
                st.Append(CvarNames[i].Replace("cl_bob", "") + "=" + _persistVal[i].ToString("0.####", CultureInfo.InvariantCulture) + " ");
            LogCfg("参数已保存 " + PersistPath + " :: " + st.ToString().Trim());
        }
        catch (Exception ex) { LogCfg("保存参数失败: " + ex.Message); _persistDirty = false; }
    }

    private void SetCvar(int i, float v)
    {
        string cmd = CvarNames[i] + " " + v.ToString("0.####", CultureInfo.InvariantCulture);
        try { Server.ExecuteCommand(cmd); } catch { }
        if (Math.Abs(ReadCvar(i) - v) <= 0.0005f) return;

        try
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p == null || !p.IsValid || p.IsBot) continue;
                p.ExecuteClientCommand(cmd);
                if (Math.Abs(ReadCvar(i) - v) <= 0.0005f) return;
            }
        }
        catch { }
        LogCfg("设置 cvar 未生效 " + CvarNames[i] + " -> " + v.ToString("0.####") + "（读回 " + ReadCvar(i).ToString("0.####") + "）");
    }

    // ================================================================ 遗留补丁清理
    private void RestoreLegacyPatches()
    {
        if (_base == IntPtr.Zero || _loc == null) return;

        // 只有「当前字节确实是当年那个补丁」才还原；跨版本时通常直接不匹配 → 一个字节都不写
        byte[] mid = ReadBytes(RVA_OLD_MID, 8);
        if (mid != null && mid.Length == 8 && mid[0] == 0xE9 && mid[5] == 0x90 && mid[6] == 0x90 && mid[7] == 0x90
            && _loc.InText(RVA_OLD_MID))
        {
            if (WriteCode(_base + RVA_OLD_MID, MID_ORIG)) LogArm("清理遗留的 0x4AAE4E detour");
        }

        byte[] gate = ReadBytes(RVA_OLD_GATE, 6);
        if (gate != null && BytesEqual(gate, OLD_GATE_PATCH) && _loc.InText(RVA_OLD_GATE))
        {
            if (WriteCode(_base + RVA_OLD_GATE, OLD_GATE_ORIG)) LogArm("清理遗留的 0x4AADE4 闸门补丁");
        }

        if (_loc.InText(0x4AADB8))
        {
            byte[] jmp = ReadBytes(0x4AADB8, 6);
            if (jmp != null && BytesEqual(jmp, GATE_JMP))
            {
                if (WriteCode(_base + 0x4AADB8, GATE_JE)) LogArm("清理遗留的 0x4AADB8 jmp，已还原 je");
            }
        }

        if (_loc.InText(0x4C49FC))
        {
            byte[] old = ReadBytes(0x4C49FC, 9);
            if (old != null && BytesEqual(old, new byte[] { 0x45, 0x0F, 0x57, 0xC9, 0x90, 0x90, 0x90, 0x90, 0x90 }))
            {
                WriteCode(_base + 0x4C49FC, new byte[] { 0xF3, 0x44, 0x0F, 0x5E, 0x0D, 0xAB, 0xE3, 0xDF, 0x00 });
                LogArm("清理遗留的 0x4C49FC 补丁");
            }
        }
    }

    // ================================================================ 工具
    private float ReadCvar(int i)
    {
        try
        {
            if (_cvs[i] != null) return _cvs[i].GetPrimitiveValue<float>();
        }
        catch { }

        if (_refs[i] == IntPtr.Zero) return -1f;
        try
        {
            if (Marshal.ReadInt32(_refs[i], 8) == 0 && Marshal.ReadInt64(_refs[i], 8) == 0) return -1f;
            if (_rvaGetFloat <= 0) return -1f;
            return Marshal.GetDelegateForFunctionPointer<GetFloatFn>(_base + _rvaGetFloat)(_refs[i]);
        }
        catch { return -1f; }
    }

    private byte[] ReadBytes(int rva, int n)
    {
        byte[] b = new byte[n];
        if (_base == IntPtr.Zero) return b;
        try { Marshal.Copy(_base + rva, b, 0, n); } catch { }
        return b;
    }

    private static bool WriteCode(IntPtr at, byte[] data)
    {
        if (data == null || data.Length == 0) return false;
        if (!VirtualProtect(at, (UIntPtr)data.Length, PAGE_EXECUTE_READWRITE, out uint old))
        {
            Log("VirtualProtect 失败 @0x" + at.ToInt64().ToString("X") + " err=" + Marshal.GetLastWin32Error());
            return false;
        }
        Marshal.Copy(data, 0, at, data.Length);
        VirtualProtect(at, (UIntPtr)data.Length, old, out _);
        FlushInstructionCache(GetCurrentProcess(), at, (UIntPtr)data.Length);
        return true;
    }

    // ================================================================ v1.38.0：pawn 手性字段强制写入
    private static bool PageUsable(IntPtr addr, bool needWrite)
    {
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery(addr, out mbi, (UIntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) == IntPtr.Zero) return false;
        if (mbi.State != MEM_COMMIT) return false;
        uint pr = (uint)mbi.Protect;
        if ((pr & PAGE_GUARD) != 0) return false;
        pr &= 0xFF;
        if (pr == PAGE_NOACCESS) return false;
        if (needWrite)
            return pr == PAGE_READWRITE || pr == PAGE_WRITECOPY || pr == PAGE_EXECUTE_READWRITE || pr == PAGE_EXECUTE_WRITECOPY;
        return true;
    }

    private static IntPtr AllocNear(IntPtr anchor, int size)
    {
        long b = anchor.ToInt64();
        const long gran = 0x10000;
        for (long off = gran; off < 0x70000000L; off += gran)
        {
            IntPtr p = VirtualAlloc(new IntPtr(b + off), (UIntPtr)size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (p != IntPtr.Zero) return p;
            if (b > off)
            {
                p = VirtualAlloc(new IntPtr(b - off), (UIntPtr)size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (p != IntPtr.Zero) return p;
            }
        }
        return IntPtr.Zero;
    }

    private static void WriteF(IntPtr p, int off, float v)
    {
        Marshal.Copy(BitConverter.GetBytes(v), 0, p + off, 4);
    }

    private float F(int off)
    {
        if (_shared == IntPtr.Zero) return 0f;
        byte[] t = new byte[4];
        Marshal.Copy(_shared + off, t, 0, 4);
        return BitConverter.ToSingle(t, 0);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string Hex(byte[] b)
    {
        return b == null ? "(null)" : BitConverter.ToString(b).Replace('-', ' ');
    }
}







