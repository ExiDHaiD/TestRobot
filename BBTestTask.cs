using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScriptSolution;
using ScriptSolution.Indicators;
using ScriptSolution.Model;
using ScriptSolution.Model.Interfaces;
using SourceEts.Config;

namespace ModulSolution.Robots
{
    public class BBTestTask : Script
    {
        public ParamOptimization volumePart1 = new ParamOptimization(1, 1, 1000, 0.1, "Объем ч.1", "Объем первой позиции контрактов");
        public ParamOptimization volumePart2 = new ParamOptimization(1, 1, 1000, 0.1, "Объем ч.2", "Объем второй позиции контрактов");

        public ParamOptimization profitPart1 = new ParamOptimization(10, 0.1, 1000, 0.1, "Цена фиксации ч.1", "Профит для закрытия первой части");

        public ParamOptimization openOffsetPoints =
            new ParamOptimization(1, 1, 1000, 1,
                "Отступ для открытия", "Величина в пунктах, на которую должен произойти пробой полосы боллинджера, для открытия позиций");

        public ParamOptimization closeOffsetPoints =
            new ParamOptimization(1, 1, 1000, 1,
                "Отступ для закрытия", "Величина в пунктах, на которую должен произойти пробой полосы боллинджера, для закрытия первой части");

        public CreateInidicator bbOpen  = new CreateInidicator(EnumIndicators.BollinderBands, 0, "BB открытия");
        public CreateInidicator bbClose = new CreateInidicator(EnumIndicators.BollinderBands, 0, "BB закрытия");

        enum TradeCondition
        {
            None,       // нет сигналов для открытия позиции
            Long,       // сигнал на открытие/закрытие длинной позиции
            Short,      // сигнал на открытие/закрытие короткой позиции
        }

        const string EnterPart1 = "part1Open";
        const string EnterPart2 = "part2Open";
        const string ExitPart1 = "part1Close";
        const string ExitPart2 = "part2Close";
        public override void Execute()
        {
            if (IndexBar < Math.Max(
                bbOpen.param.LinesIndicators[0].LineParam[0].Value, 
                bbClose.param.LinesIndicators[0].LineParam[0].Value))
                return;

            SetProfitPart1();

            var c = CheckOpenCondition();
            if (c != TradeCondition.None)
            {
                var part2 = FindPosByOpenSignal(EnterPart2);                
                if ((part2 == null) ||
                    part2.IsLong && (c == TradeCondition.Long) || // разрешаем повторное открытие первой части
                    part2.IsShort && (c == TradeCondition.Short))
                {
                    var part1 = FindPosByOpenSignal(EnterPart1);
                    // Запрещаем повторное открытие второй части
                    if ((part2 == null) && 
                        (part1 == null))
                        OpenPosition(c, volumePart2.Value, EnterPart2);

                    if(part1 == null)
                        OpenPosition(c, volumePart1.Value, EnterPart1); 
                }
            }

            // Проверяем услове закрытия второй части
            c = CheckCloseCondition();
            if (c != TradeCondition.None)
                ClosePosition(c, EnterPart2, ExitPart2);
        }

		TradeCondition CheckOpenCondition()
        {
            if (Candles.LowSeries[IndexBar] < bbOpen.param.LinesIndicators[2].PriceSeries[IndexBar])
                return TradeCondition.Long;

            if (Candles.HighSeries[IndexBar] > bbOpen.param.LinesIndicators[1].PriceSeries[IndexBar])
                return TradeCondition.Short;

            return TradeCondition.None;
        }

        TradeCondition CheckCloseCondition()
        {
            if (Candles.LowSeries[IndexBar] < bbClose.param.LinesIndicators[2].PriceSeries[IndexBar])
                return TradeCondition.Short;

            if (Candles.HighSeries[IndexBar] > bbClose.param.LinesIndicators[1].PriceSeries[IndexBar])
                return TradeCondition.Long;

            return TradeCondition.None;
        }

        void OpenPosition(TradeCondition condition, double lot, string signal)
        {
            var openOffset = closeOffsetPoints.ValueInt * FinInfo.Security.MinStep;

            double entryLongPrice = bbOpen.param.LinesIndicators[2].PriceSeries[IndexBar] - openOffset;
            double entryShortPrice = bbOpen.param.LinesIndicators[1].PriceSeries[IndexBar] + openOffset;

            if (condition == TradeCondition.Long)
                BuyAtLimit(IndexBar + 1, entryLongPrice, lot, signal);

            if (condition == TradeCondition.Short)
                ShortAtLimit(IndexBar + 1, entryShortPrice, lot, signal);
        }

        IPosition FindPosByOpenSignal(string openSignal)
        {
            IPosition opened = LongPos.Find(p => p.EntryNameSignal == openSignal);
            if (opened == null)
                opened = ShortPos.Find(p => p.EntryNameSignal == openSignal);
            return opened;
        }

        void ClosePosition(TradeCondition condition, string openSignal, string closeSignal)
        {
            var opened = FindPosByOpenSignal(openSignal);
            if (opened != null)
            {
                if (opened.IsLong && (condition == TradeCondition.Long))
                    SellAtMarket(IndexBar + 1, opened, closeSignal);

                if (opened.IsShort && (condition == TradeCondition.Short))
                    CoverAtMarket(IndexBar + 1, opened, closeSignal);
            }
        }

        void SetProfitPart1()
        {
            var part1 = FindPosByOpenSignal(EnterPart1);

            if (part1 != null)
            {
                var closeOffset = closeOffsetPoints.ValueInt * FinInfo.Security.MinStep;
                var priceUp = bbClose.param.LinesIndicators[1].PriceSeries[IndexBar];
                var priceDown = bbClose.param.LinesIndicators[2].PriceSeries[IndexBar];

                if (part1.IsLong)
                {
                    double exitPrice = part1.EntryPrice + profitPart1.Value;
                    if (Candles.HighSeries[IndexBar] > priceUp)
                    {
                        exitPrice = Math.Min(exitPrice, priceUp + closeOffset);
                    }
                    SellAtProfit(IndexBar + 1, part1, exitPrice, ExitPart1);
                }

                if (part1.IsShort)
                {
                    double exitPrice = part1.EntryPrice - profitPart1.Value;
                    if (Candles.LowSeries[IndexBar] < priceDown)
                    {
                        exitPrice = Math.Max(exitPrice, priceDown - closeOffset);
                    }
                    CoverAtProfit(IndexBar + 1, part1, exitPrice, ExitPart1);
                }
            }
        }

        public override void GetAttributesStratetgy()
        {
            DesParamStratetgy.Version = "1.0.0.0";
            DesParamStratetgy.DateRelease = "26.05.2022";
            DesParamStratetgy.Description = "Открытие позиции двумя частями. " +
				"Открытие лонг (две части) происходит при пересечении ценой нижней линии боллинжера " +
				"(берется значение последней закрытой свечи) для входа, " +
				"цена входа = нижняя линия боллинжера - отступ на открытие. " +
				"Закрытие первой части происходит при достижении профита или при пересечении верхней линии боллинжера  " +
				"(берется значение последней закрытой свечи) на выход на заданный отступ закрытие. " +
				"Вторая часть позиции только при пересечении верхней линии боллинжера  " +
				"(берется значение последней закрытой свечи) для выхода из позиции. " +
				"Если первая часть позиции была закрыта, то ее повторное и последующее открытие " +
				"возможно при повторном пробитии нижней полосы боллинжера" +
				"(берется значение последней закрытой свечи) минус отступ на открытие. " +
				"Для Шорт зеркальная ситуация.";

            DesParamStratetgy.Change = "";
            DesParamStratetgy.NameStrategy = "Боллинджер, пробой";
        }
	}
}