using System;
using System.Collections.Generic;

namespace GTS_800
{
    public class Gts_AxisCfg
    {
        public List<CardAxisCfg> CardAxisCfgs = new List<CardAxisCfg>();

        public void Validate()
        {
            CardAxisCfgs.ForEach(x => x.Validate());
        }
    }

    public class CardAxisCfg
    {
        public int CardId { get; set; } = -1;
        public List<AxisCfg> AxisCfgs = new List<AxisCfg>();

        public void Validate()
        {
            AxisCfgs.ForEach(x =>
            {
                try
                {
                    x.Validate();
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new ArgumentOutOfRangeException($"轴卡 {CardId} 中的参数设置错误，{ex.Message}", ex);
                }
            });
        }
    }

    public class AxisCfg
    {
        #region Properties

        public int AxisIndex { get; set; } = -1;

        public int HomeMode { get; set; } = -1;

        public int HomeDir { get; set; } = -2;

        public int SearchHomeDistance { get; set; } = -1;

        public int HomeOffset { get; set; } = -1;

        public int EscapeStep { get; set; } = -1;

        public int Pad2_1 { get; set; } = -1;

        #endregion

        #region Methods

        public void Validate()
        {
            var errParam = string.Empty;

            //TODO 根据手册查询每个参数的范围

            if (HomeMode == -1)
                errParam = nameof(HomeMode);
            if (HomeDir != -1 && HomeDir != 1)
                errParam = nameof(HomeDir);
            if (SearchHomeDistance == -1)
                errParam = nameof(SearchHomeDistance);
            if (HomeOffset == -1)
                errParam = nameof(HomeOffset);
            if (EscapeStep == -1)
                errParam = nameof(EscapeStep);
            if (Pad2_1 == -1)
                errParam = nameof(Pad2_1);

            if (!string.IsNullOrEmpty(errParam))
                throw new ArgumentOutOfRangeException(errParam, $"轴 {AxisIndex} 参数 {errParam} 错误。");
        }

        #endregion
    }
}