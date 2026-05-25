using EBookDashboard.Application.Kdp.DTOs;

namespace EBookDashboard.Application.Kdp.Interfaces;

public interface IKdpCoverDimensionService
{
    /// <summary>
    /// Computes KDP paperback full-wrap dimensions for any valid page count.
    /// Spine width is dynamic: pageCount × spineInchesPerPage, rounded like KDP.
    /// </summary>
    KdpCalculateResponse Calculate(KdpCalculateRequest request);
}
