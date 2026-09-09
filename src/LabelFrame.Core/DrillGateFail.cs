namespace LabelFrame.Core;

// 门禁演练专用：故意语法错误，用于验证「必需检查失败 → 平台禁止合并」。不合并本 PR。
public static class DrillGateFail
{
    public static int Broken() => 1 + ;
}
