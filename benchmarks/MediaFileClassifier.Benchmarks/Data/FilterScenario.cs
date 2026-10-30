namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// The filters each query runs with. They mirror the search screen: no filter, a broad one,
/// and a narrow one.
/// </summary>
internal enum FilterScenario
{
    /// <summary>
    /// The whole library.
    /// </summary>
    None,

    /// <summary>
    /// Library 1, photos taken since 2020. About 16% of the items.
    /// </summary>
    Broad,

    /// <summary>
    /// Videos in library 2. About 6% of the items.
    /// </summary>
    Narrow,
}
