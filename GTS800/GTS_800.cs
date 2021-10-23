using System;
using System.Collections.Generic;
using System.Threading;
using APAS__MotionControl;
using APAS__MotionControl.Core;
using log4net;
using static gts.mc;
using System.IO;
using GTS_800;
using Newtonsoft.Json;
using System.Linq;

/*
注意：

1. 应用该模板时，请注意将命名空间更改为实际名称。
2. 该类中的所有Childxxx()方法中，请勿阻塞式调用实际的运动控制器库函数，因为在APAS主程序中，可能会同时调用多个轴同步移动。
3. 请保证所有的Childxxx()方法为线程安全。

*/

namespace APAS__MotionLib_Template
{
    public class GTS_800 : MotionControllerBase
    {
        #region Variables

        private readonly short _mCardId;
        private Gts_AxisCfg _gtsAxisCfg;
        private readonly string _configFileGts = "gts800.cfg";
        private readonly string _configFileAxis = "Gts800_AxisCfg.json";

        #endregion

        #region Constructors

        /// <summary>
        /// 注意：类名应为 “MotionController",请勿更改。
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="config"></param>
        /// <param name="logger"></param>
        public GTS_800(string portName, int baudRate, string config, ILog logger) : base(portName, baudRate, config,
            logger)
        {
            //TODO 此处初始化控制器参数；如果下列参数动态读取，则可在ChildInit()函数中赋值。

            if (!short.TryParse(portName, out _mCardId))
                _mCardId = 0;

            var configs = config.Split(',');
            if (configs.Length == 2)
            {
                _configFileGts = configs[0];
                _configFileAxis = configs[1];
            }

            AxisCount = 8; // 最大轴数
            MaxAnalogInputChannels = 8; // 最大模拟量输入通道数
            MaxAnalogOutputChannels = 0; // 最大模拟量输出通道数
            MaxDigitalInputChannels = 16; // 最大数字量输入通道数
            MaxDigitalOutputChannels = 16; // 最大数字量输出通道数
        }

        #endregion

        #region Overrided Methods

        /// <summary>
        /// 初始化指定轴卡。
        /// </summary>
        protected override void ChildInit()
        {
            //TODO 1.初始化运动控制器对象，例如凌华轴卡、固高轴卡等。
            // 例如：初始化固高轴卡：gts.mc.GT_Open(portName, 1);

            //TODO 2.读取控制器固件版本，并赋值到属性 FwVersion

            //TODO 3.读取每个轴的信息，构建 InnerAxisInfoCollection，包括轴号和固件版本号。
            // 注意：InnerAxisInfoCollection 已在基类的构造函数中初始化
            // 例如： InnerAxisInfoCollection.Add(new AxisInfo(1, new Version(1, 0, 0)));

            //TODO 4.需要完成函数 ChildUpdateStatus()，否则会报NotImplementException异常。
            var rtn = GT_Open((short) _mCardId, 0, 1);
            CommandRtnCheck(rtn, nameof(GT_Open));

            rtn = GT_Reset((short) _mCardId);
            CommandRtnCheck(rtn, nameof(GT_Reset));

            var fullName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFileGts);
            if (!File.Exists(fullName))
                throw new FileNotFoundException($"无法找到轴卡配置文件 {fullName}");
            rtn = GT_LoadConfig((short)_mCardId, fullName);
            CommandRtnCheck(rtn, nameof(GT_LoadConfig));

            rtn = GT_ClrSts((short) _mCardId, 1, 8);
            CommandRtnCheck(rtn, nameof(GT_ClrSts));

            LoadAxisConfiguration();

            // 强制自动ServoOn所有轴，不管是否为伺服轴
            var cfg = _gtsAxisCfg.CardAxisCfgs.FirstOrDefault(x => x.CardId == _mCardId);
            if(cfg!=null)
            {
                foreach (var mem in cfg.AxisCfgs)
                {
                    GT_AxisOn((short)_mCardId, (short)mem.AxisIndex);
                }
            }
            
        }

        /// <summary>
        /// 设置指定轴的加速度。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="acc">加速度值</param>
        protected override void ChildSetAcceleration(int axis, double acc)
        {
            var rtn = GT_GetTrapPrm(_mCardId, (short) axis, out var trapPrm);
            CommandRtnCheck(rtn, "GT_GetTrapPrm  in ChildSetAcceleration");
            trapPrm.acc = acc;
            rtn = GT_SetTrapPrm(_mCardId, (short) axis, ref trapPrm);
            CommandRtnCheck(rtn, "GT_SetTrapPrm  in ChildSetAcceleration");
        }

        /// <summary>
        /// 设置指定轴的减速度。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="dec">减速度值</param>
        protected override void ChildSetDeceleration(int axis, double dec)
        {
            var rtn = GT_GetTrapPrm(_mCardId, (short) axis, out var trapPrm);
            CommandRtnCheck(rtn, "GT_GetTrapPrm  in ChildSetAcceleration");
            trapPrm.acc = dec;
            rtn = GT_SetTrapPrm(_mCardId, (short) axis, ref trapPrm);
            CommandRtnCheck(rtn, "GT_SetTrapPrm  in ChildSetAcceleration");
        }

        /// <summary>
        /// 指定轴回机械零点。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="hiSpeed">快速找机械原点的速度值。如不适用请忽略。</param>
        /// <param name="creepSpeed">找到机械原点后返回零位的爬行速度。如不适用请忽略。</param>
        protected override void ChildHome(int axis, double hiSpeed, double creepSpeed)
        {
            /*
             * 耗时操作。当执行操作时，请轮询轴状态，并调用 RaiseAxisStateUpdatedEvent(new AxisStatusArgs(axis, xxx)); 
             * 以实时刷新UI上的位置。       
             */

            THomeStatus homeStatus;
            var homeParam = CreateAxisParam((short) axis);

            homeParam.acc = 5;
            homeParam.dec = 5;
            homeParam.velHigh = hiSpeed;
            homeParam.velLow = 1;
            var rtn = GT_GoHome(_mCardId, (short) axis, ref homeParam); //启动回零
            CommandRtnCheck(rtn, "GT_GoHome");
            double p;
            do
            {
                Thread.Sleep(50);
                rtn = GT_GetHomeStatus(_mCardId, (short) axis, out homeStatus);
                CommandRtnCheck(rtn, "GT_GetHomeStatus");

                ChildUpdateAbsPosition(axis);
            } while (homeStatus.run != 0);

            Thread.Sleep(500);
            rtn = GT_ZeroPos(_mCardId, (short) axis, 1);
            CommandRtnCheck(rtn, nameof(GT_ZeroPos));
            
            rtn = GT_ClrSts(_mCardId, (short)axis, 1);
            CommandRtnCheck(rtn, nameof(GT_ClrSts));

            // 刷新位置
            var pos = ChildUpdateAbsPosition(axis);

            // 刷新IsHomed状态和 Servo On状态
            RaiseAxisStateUpdatedEvent(new AxisStatusArgs(axis, pos, true, true));
        }

        /// <summary>
        /// 移动指定轴（相对移动模式）。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="speed">移动速度。该速度根据APAS主程序的配置文件计算得到。计算方法为MaxSpeed * 速度百分比。</param>
        /// <param name="distance">相对移动的距离。该距离已被APAS主程序转换为轴卡对应的实际单位。例如对于脉冲方式，
        /// 该值已转换为步数；对于伺服系统，该值已转换为实际距离。</param>
        /// <param name="fastMoveRequested">是否启用快速移动模式。如不适用请忽略。</param>
        /// <param name="microstepRate">当启用快速移动模式时的驱动器细分比值。如不适用请忽略。</param>
        protected override void ChildMove(
            int axis,
            double speed,
            double distance,
            bool fastMoveRequested = false,
            double microstepRate = 0)
        {
            /*
             * 耗时操作。当执行操作时，请轮询轴状态，并调用 RaiseAxisStateUpdatedEvent(new AxisStatusArgs(axis, xxx)); 
             * 以实时刷新UI上的位置。       
            */

            var rtn = GT_PrfTrap(_mCardId, (short) axis);
            CommandRtnCheck(rtn, nameof(GT_PrfTrap));

            rtn = GT_SetVel(_mCardId, (short) axis, speed);
            CommandRtnCheck(rtn, nameof(GT_SetVel));

            rtn = GT_GetAxisEncPos(_mCardId, (short) axis, out var encPosition, 1, out var clk);
            CommandRtnCheck(rtn, nameof(GT_GetAxisEncPos));

            rtn = GT_SetPos(_mCardId, (short) axis, (int) encPosition + (int) distance);
            CommandRtnCheck(rtn, nameof(GT_SetPos));

            rtn = GT_Update(_mCardId, 1 << (axis - 1));
            CommandRtnCheck(rtn, nameof(GT_Update));

            var moveStatus = 0;
            do
            {
                rtn = GT_GetSts(_mCardId, (short) axis, out moveStatus, 1, out var pClock);
                CommandRtnCheck(rtn, nameof(GT_GetSts));

                ChildUpdateAbsPosition(axis);
                Thread.Sleep(100);
            } while ((moveStatus & 0x400) != 0);

            
            Thread.Sleep(10);
            ChildUpdateAbsPosition(axis);
            CheckAxisStatus((short)axis);
        }


        /// <summary>
        /// 移动指定轴到绝对位置（绝对移动模式）。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="speed">移动速度</param>
        /// <param name="position">绝对目标位置</param>
        /// <param name="fastMoveRequested">是否启用快速移动模式。如不适用请忽略。</param>
        /// <param name="microstepRate">当启用快速移动模式时的驱动器细分比值。如不适用请忽略。</param>
        protected override void ChildMoveAbs(
            int axis,
            double speed,
            double position,
            bool fastMoveRequested = false,
            double microstepRate = 0)
        {
            var rtn = GT_PrfTrap(_mCardId, (short) axis);
            CommandRtnCheck(rtn, nameof(GT_PrfTrap));

            rtn = GT_SetVel(_mCardId, (short) axis, speed);
            CommandRtnCheck(rtn, nameof(GT_SetVel));

            rtn = GT_SetPos((short) _mCardId, (short) axis, (int) position);
            CommandRtnCheck(rtn, nameof(GT_SetPos));

            rtn = GT_Update((short) _mCardId, 1 << ((int) axis - 1));
            CommandRtnCheck(rtn, nameof(GT_Update));

            int movStatus;
            do
            {
                rtn = GT_GetSts(_mCardId, (short) axis, out movStatus, 1, out _);
                ChildUpdateAbsPosition(axis);
                Thread.Sleep(100);
            } while ((movStatus & 0x400) != 0);

            Thread.Sleep(500);
            CheckAxisStatus((short)axis);
        }

        /// <summary>
        /// 开启励磁。
        /// </summary>
        /// <param name="axis">轴号</param>
        protected override void ChildServoOn(int axis)
        {
            var rtn = GT_AxisOn(_mCardId, (short) axis);
            CommandRtnCheck(rtn, nameof(GT_AxisOn));

            //TODO Check the status of the axis to report the errors;
        }

        /// <summary>
        /// 关闭励磁。
        /// </summary>
        /// <param name="axis">轴号</param>
        protected override void ChildServoOff(int axis)
        {
            var rtn = GT_AxisOff(_mCardId, (short) axis);
            CommandRtnCheck(rtn, nameof(GT_AxisOff));
        }

        /// <summary>
        /// 读取最新的绝对位置。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <returns>最新绝对位置</returns>
        protected override double ChildUpdateAbsPosition(int axis)
        {
            //pClock 读取控制器时钟，默认值为：NULL，即不用读取控制器时钟
            //count  读取的轴数，默认为 1。正整数。

            var rtn = GT_GetAxisEncPos(_mCardId, (short) axis, out var pValue, 1, out var pClock);
            CommandRtnCheck(rtn, nameof(GT_GetAxisEncPos));
            
            RaiseAxisStateUpdatedEvent(new AxisStatusArgs(axis, pValue));

            return pValue;
        }

        /// <summary>
        /// 更新指定轴状态。
        /// <para>注意：请在该函数调用RaiseAxisStateUpdatedEvent()函数，以通知APAS主程序当前轴的状态已更新。</para>
        /// </summary>
        /// <param name="axis">轴号</param>
        protected override void ChildUpdateStatus(int axis)
        {
            // 注意:
            // 1. 读取完状态后请调用 RaiseAxisStateUpdatedEvent 函数。
            // 2. 实例化 AxisStatusArgs 时请传递所有参数。
            
            // RaiseAxisStateUpdatedEvent(new AxisStatusArgs(axis, pValue, false, false));
            
        }

        /// <summary>
        /// 更新所有轴状态。
        /// <see cref="ChildUpdateStatus(int)"/>
        /// </summary>
        protected override void ChildUpdateStatus()
        {
            // 注意:
            // 1. 读取完状态后请循环调用 RaiseAxisStateUpdatedEvent 函数，
            //    例如对于 8 轴轴卡，请调用针对8个轴调用 8 次 RaiseAxisStateUpdatedEvent 函数。
            // 2. 实例化 AxisStatusArgs 时请传递所有参数。
            //// RaiseAxisStateUpdatedEvent(new AxisStatusArgs(int.MinValue, double.NaN, false, false));
        }


        /// <summary>
        /// 清除指定轴的错误。
        /// </summary>
        /// <param name="axis">轴号</param>
        protected override void ChildResetFault(int axis)
        {
            var rtn = GT_ClrSts((short) _mCardId, (short) axis, 1);
            CommandRtnCheck(rtn, "GT_ClrSts");

            //TODO Check the status of the axis to report the errors
        }


        #region IO Controller

        /// <summary>
        /// 设置指定数字输出端口的状态。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="isOn">是否设置为有效电平</param>
        protected override void ChildSetDigitalOutput(int port, bool isOn)
        {
            //MC_ENABLE(该宏定义为 10)：驱动器使能。
            //MC_CLEAR(该宏定义为 11)：报警清除。
            //MC_GPO(该宏定义为 12)：通用输出。

            var rtn = GT_SetDoBit(_mCardId, MC_GPO, (short) (port + 1), isOn ? (short) 0 : (short) 1);

            CommandRtnCheck(rtn, "GT_SetDoBit");
        }

        /// <summary>
        /// 读取指定数字输出端口。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <returns>端口状态。True表示端口输出为有效电平。</returns>
        protected override bool ChildReadDigitalOutput(int port)
        {
            //MC_ENABLE(该宏定义为 10)：驱动器使能。
            //MC_CLEAR(该宏定义为 11)：报警清除。
            //MC_GPO(该宏定义为 12)：通用输出。
            var rtn = GT_GetDo(_mCardId, MC_GPO, out var pValue);
            CommandRtnCheck(rtn, "GT_GetDo");
            return (pValue & (1 << port)) == 0;
        }

        /// <summary>
        /// 读取所有数字输出端口。
        /// </summary>
        /// <returns>端口状态列表。True表示端口输出为有效电平。</returns>
        protected override IReadOnlyList<bool> ChildReadDigitalOutput()
        {
            var rtn = GT_GetDo(_mCardId, MC_GPO, out var pValue);
            CommandRtnCheck(rtn, "GT_GetDo");
            var states = new bool[16];
            for (var i = 0; i < 16; i++)
                states[i] = (pValue & (1 << i)) == 0;

            return states;
        }

        /// <summary>
        /// 读取指定数字输入端口。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <returns>端口状态。True表示端口输出为有效电平。</returns>
        protected override bool ChildReadDigitalInput(int port)
        {
            var rtn = GT_GetDi(_mCardId, MC_GPI, out var pValue);
            CommandRtnCheck(rtn, "GT_GetDi");
            return (pValue & (1 << port)) == 0;
        }

        /// <summary>
        /// 读取所有数字输入端口。
        /// </summary>
        /// <returns>端口状态列表。True表示端口输出为有效电平。</returns>
        protected override IReadOnlyList<bool> ChildReadDigitalInput()
        {
            var rtn = GT_GetDi(_mCardId, MC_GPI, out var pValue);
            CommandRtnCheck(rtn, "GT_GetDi");
            var states = new bool[16];
            for (var i = 0; i < 16; i++)
                states[i] = (pValue & (1 << i)) == 0;

            return states;
        }

        #endregion

        #region Analog Controller

        /// <summary>
        /// 读取所有模拟输入端口的电压值。
        /// </summary>
        /// <returns>电压值列表。</returns>
        protected override IReadOnlyList<double> ChildReadAnalogInput()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 读取指定模拟输入端口的电压值。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <returns></returns>
        protected override double ChildReadAnalogInput(int port)
        {
            var rtn = GT_GetAdc(_mCardId, (short) port, out var pValue, 1, out var pClock);
            CommandRtnCheck(rtn, "GT_GetAdc");
            return pValue;
        }

        /// <summary>
        /// 读取所有模拟输出端口的电压值。
        /// </summary>
        /// <returns>电压值列表。</returns>
        protected override IReadOnlyList<double> ChildReadAnalogOutput()
        {
            var datas = new short[1];
            var rtn = GT_GetDac(_mCardId, 0, out datas[0], 1, out var pClock);
            CommandRtnCheck(rtn, "GT_GetDac");
            var vRtn = new double[datas.Length];
            for (var i = 0; i < datas.Length; i++)
                vRtn[i] = datas[i];

            return vRtn;
        }

        /// <summary>
        /// 读取指定模拟输出端口的电压值。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <returns></returns>
        protected override double ChildReadAnalogOutput(int port)
        {
            var rtn = GT_GetDac(_mCardId, (short) port, out var pValue, 1, out var pClock);
            CommandRtnCheck(rtn, "GT_GetAdc");
            return pValue;
        }

        /// <summary>
        /// 设置指定模拟输出端口的电压值。
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="value">电压值</param>
        protected override void ChildSetAnalogOutput(int port, double value)
        {
            var data = (short) value;

            var rtn = GT_SetDac(_mCardId, (short) port, ref data, 1);
            CommandRtnCheck(rtn, "GT_SetDac");
        }

        /// <summary>
        /// 打开指定模拟输出端口的输出。
        /// </summary>
        /// <param name="port">端口号</param>
        protected override void ChildAnalogOutputOn(int port)
        {
        }

        /// <summary>
        /// 关闭指定模拟输出端口的输出。
        /// </summary>
        /// <param name="port">端口号</param>
        protected override void ChildAnalogOutputOff(int port)
        {
        }

        #endregion

        /// <summary>
        /// 在指定轴上执行自动接触检测功能。
        /// <para>该功能适用于Irixi M12控制器。</para>
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="analogInputPort">模拟输入端口号</param>
        /// <param name="vth">阈值电压</param>
        /// <param name="distance">最大移动距离</param>
        /// <param name="speed">移动速度</param>
        protected override void ChildAutoTouch(int axis, int analogInputPort, double vth, double distance, double speed)
        {
        }

        /// <summary>
        /// 执行快速线性扫描。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="range">扫描范围</param>
        /// <param name="interval">反馈信号采样间隔</param>
        /// <param name="speed">移动速度</param>
        /// <param name="analogCapture">反馈信号捕获端口</param>
        /// <param name="scanResult">扫描结果列表（X:位置，Y:反馈信号）</param>
        protected override void ChildStartFast1D(
            int axis,
            double range,
            double interval,
            double speed,
            int analogCapture,
            out IEnumerable<Point2D> scanResult)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 执行双通道快速线性扫描。
        /// </summary>
        /// <param name="axis">轴号</param>
        /// <param name="range">扫描范围</param>
        /// <param name="interval">第1路反馈信号采样间隔</param>
        /// <param name="speed">移动速度</param>
        /// <param name="analogCapture">反馈信号捕获端口</param>
        /// <param name="scanResult">第1路扫描结果列表（X:位置，Y:反馈信号）</param>
        /// <param name="analogCapture2">第2路反馈信号采样间隔</param>
        /// <param name="scanResult2">第2路扫描结果列表（X:位置，Y:反馈信号）</param>
        protected override void ChildStartFast1D(
            int axis,
            double range,
            double interval,
            double speed,
            int analogCapture,
            out IEnumerable<Point2D> scanResult,
            int analogCapture2,
            out IEnumerable<Point2D> scanResult2)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 执行快速盲扫。
        /// </summary>
        /// <param name="hAxis">水平轴轴号</param>
        /// <param name="vAxis">垂直轴轴号</param>
        /// <param name="range">扫描区域（正方形）的边长</param>
        /// <param name="gap">扫描螺旋线路的间隔</param>
        /// <param name="interval">每条扫描线上反馈信号采样间隔</param>
        /// <param name="hSpeed">水平轴扫描速度</param>
        /// <param name="vSpeed">垂直轴扫描速度</param>
        /// <param name="analogCapture">反馈信号捕获端口</param>
        /// <param name="scanResult">扫描结果列表（X:水平轴坐标，Y:垂直轴坐标，Z:反馈信号）</param>
        protected override void ChildStartBlindSearch(
            int hAxis,
            int vAxis,
            double range,
            double gap,
            double interval,
            double hSpeed,
            double vSpeed,
            int analogCapture,
            out IEnumerable<Point3D> scanResult)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 停止所有轴移动。
        /// </summary>
        protected override void ChildStop()
        {
            GT_Stop(_mCardId, 0xff, 0);
        }

        /// <summary>
        /// 关闭运动控制器，并销毁运动控制器实例。
        /// </summary>
        protected override void ChildDispose()
        {
        }

        /// <summary>
        /// 检查移动速度。
        /// <para>如无需检查，请保持该函数为空。</para>
        /// </summary>
        /// <param name="speed">速度</param>
        protected override void CheckSpeed(double speed)
        {
        }

        /// <summary>
        /// 检查控制器状态。
        /// </summary>
        protected override void CheckController()
        {
            base.CheckController(); // 请勿删除该行。
        }

        #endregion

        private void CommandRtnCheck(short rtn, string commandName)
        {
            var conStr = $"cardNum : {_mCardId};";
            var errorInfo = string.Empty;
            switch (rtn)
            {
                case 0:

                    break;
                case 1:
                    errorInfo = $"{commandName} 指令执行错误";
                    break;
                case 2:
                    errorInfo = $"{commandName} 指令license不支持";

                    break;
                case 7:
                    errorInfo = $"{commandName} 指令参数错误";

                    break;
                case 8:
                    errorInfo = $"{commandName} 指令DSP固件不支持";

                    break;
                case -1:
                case -2:
                case -3:
                case -4:
                case -5:
                    errorInfo = $"{commandName} 指令与控制卡通讯失败";

                    break;
                case -6:
                    errorInfo = $"打开控制器失败";

                    break;
                case -7:
                    errorInfo = $"运动控制器没有相应";

                    break;
                case -8:
                    errorInfo = $"{commandName} 指令多线程资源忙";
                    break;
                default:
                    errorInfo = $"{commandName} 指令返回未知错误";

                    break;
            }

            if (!string.IsNullOrEmpty(errorInfo))
                throw new Exception(conStr + errorInfo);
        }

        /// <summary>
        /// Check the status of the specified axis, which contains the errors on the axis.
        /// </summary>
        /// <param name="axis"></param>
        private void CheckAxisStatus(short axis)
        {
            var rtn = GT_GetSts(_mCardId, (short)axis, out var pSts, 1, out var _);
            CommandRtnCheck(rtn, nameof(GT_GetSts));


            if ((pSts & 0x2) != 0)
                throw new Exception($"伺服报警");

            if ((pSts & 0x10) != 0)
                throw new Exception($"跟随误差越线");

            if ((pSts & 0x20) != 0)
                throw new Exception($"正限位触发");

            if ((pSts & 0x40) != 0)
                throw new Exception($"负限位触发");
            //if ((pSts & 0x80) != 0)
            //    throw new Exception($"第{_mCardId}号卡，第{axis}个轴 平滑停止");
            if ((pSts & 0x100) != 0)
                throw new Exception($"紧急停止状态");

            if ((pSts & 0x200) == 0)
                throw new Exception($"伺服未使能");

            if ((pSts & 0x400) != 0)
                throw new Exception($"规划器正在运动");
        }


        private THomePrm CreateAxisParam(short axisIndex)
        {
            var rtn = GT_GetHomePrm(_mCardId, axisIndex, out var homeParam);

            var cfg = _gtsAxisCfg.CardAxisCfgs.FirstOrDefault(x => x.CardId == _mCardId)
                ?.AxisCfgs
                .FirstOrDefault(x => x.AxisIndex == axisIndex);

            if (cfg == null)
                throw new Exception($"找不到轴卡({_mCardId})的轴({axisIndex})的配置文件");


            cfg.Validate();

            homeParam.mode = (short) cfg.HomeMode; //回零方式
            homeParam.moveDir = (short) cfg.HomeDir; //回零方向

            homeParam.searchHomeDistance = cfg.SearchHomeDistance; //搜搜距离
            homeParam.homeOffset = cfg.HomeOffset; //偏移距离
            homeParam.escapeStep = cfg.EscapeStep;
            homeParam.pad2_1 = (short) cfg.Pad2_1; //此参数表示如果回零时sensor处于原点位置上，也会再继续回原点动作，否者会异常

            return homeParam;
        }

        private void LoadAxisConfiguration()
        {
            var fileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFileAxis);
            if (!File.Exists(fileName))
                return;

            var jsonInfo = File.ReadAllText(fileName);
            try
            {
                _gtsAxisCfg = JsonConvert.DeserializeObject<Gts_AxisCfg>(jsonInfo);
            }
            catch (Exception ex)
            {
                throw new Exception($"无法加载轴参数配置文件 {fileName}, {ex.Message}");
            }
        }
    }
}