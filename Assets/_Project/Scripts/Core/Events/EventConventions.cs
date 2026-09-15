// 职责：框架事件的约定说明（本文件只有注释，没有类型）。
// 为什么新建：约定必须放在写事件的人一眼能看到的地方；塞进某个事件结构体的文件头会随那个事件
// 一起被改名/删除，塞进 ai-docs 又离代码太远。独立一份纯注释文件是最不易腐化的载体。
//
// ============================ 事件约定（写事件前先读这 6 条） ============================
//
// 1. 传输方式：一律走 MessagePipe 的 IPublisher<T> / ISubscriber<T>，由 VContainer 注入，
//    不用 C# static event，不用 SendMessage。
//
// 2. 事件类型：`public readonly struct`，字段只读（构造函数赋值 + 只读属性），不可变。
//    用 struct 是为了避免每次发布产生堆分配；只读是为了订阅者之间不会互相改到同一份数据。
//
// 3. 命名：`XxxEvent`，描述「已经发生的事实」（BootCompletedEvent、GameStateChangedEvent），
//    不是命令（不要出现 DoXxxEvent / RequestXxxEvent 这类祈使命名——那是意图对象，走别的通道）。
//
// 4. 注册：在作用域的 Configure 里
//        var options = builder.RegisterMessagePipe();
//        builder.RegisterMessageBroker<XxxEvent>(options);
//    全局事件注册在根作用域 GameLifetimeScope；模块内部事件注册在模块自己的子作用域。
//
// 5. 订阅句柄必须托管，禁止裸订阅：
//        var bag = DisposableBag.CreateBuilder();
//        subscriber.Subscribe(e => { ... }).AddTo(bag);
//        disposable = bag.Build();          // 在 Dispose / OnDestroy 里释放
//    句柄丢了就退订不掉，作用域销毁后回调还在跑，拿着已销毁对象报空引用。
//
// 6. 不要在事件回调里再同步发布同一个事件；需要串联就把后一步排进队列或用 async 流程，
//    否则重入会把订阅者列表搅乱。
//
// ======================================================================================
