using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DerivativesPricerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimulationController : ControllerBase
{
    private const int DefaultPaths = 100;
    private const int DefaultSteps = 200;
    private const double DefaultS0 = 100.0;
    private const double DefaultMu = 0.08;    // 8% drift
    private const double DefaultSigma = 0.20; // 20% vol
    private const double DefaultT = 1.0;      // 1 year

    [HttpGet("brownian-motion")]
    public IActionResult GetBrownianMotion(
        [FromQuery] int paths = DefaultPaths,
        [FromQuery] int steps = DefaultSteps,
        [FromQuery] double s0 = DefaultS0,
        [FromQuery] double mu = DefaultMu,
        [FromQuery] double sigma = DefaultSigma,
        [FromQuery] double t = DefaultT)
    {
        var random = new Random();
        var dt = t / steps;
        var drift = (mu - 0.5 * sigma * sigma) * dt;
        var vol = sigma * Math.Sqrt(dt);

        var allPaths = new List<List<double>>();

        for (int p = 0; p < paths; p++)
        {
            var path = new List<double> { s0 };
            var current = s0;

            for (int step = 0; step < steps; step++)
            {
                var z = NextGaussian(random); // standard normal
                current *= Math.Exp(drift + vol * z);
                path.Add(current);
            }

            allPaths.Add(path);
        }

        return Ok(new
        {
            paths = allPaths,           // List<List<double>>
            timePoints = Enumerable.Range(0, steps + 1).Select(i => i * dt).ToArray()
        });
    }


    [HttpGet("ornstein-uhlenbeck")]
    public IActionResult GetOrnsteinUhlenbeck(
        [FromQuery] int paths = 100,
        [FromQuery] int steps = 200,
        [FromQuery] double x0 = 1.0,
        [FromQuery] double kappa = 3.0,     // mean reversion speed
        [FromQuery] double theta = 1.0,     // long-term mean
        [FromQuery] double sigma = 0.15,    // volatility
        [FromQuery] double t = 1.0)         // total time horizon in years
    {
        var random = new Random();
        var dt = t / steps;
        var sqrtDt = Math.Sqrt(dt);

        var allPaths = new List<List<double>>();

        for (int p = 0; p < paths; p++)
        {
            var path = new List<double> { x0 };
            double current = x0;

            for (int step = 0; step < steps; step++)
            {
                double z = NextGaussian(random);
                current += kappa * (theta - current) * dt + sigma * sqrtDt * z;
                path.Add(current);
            }

            allPaths.Add(path);
        }

        var timePoints = Enumerable.Range(0, steps + 1)
            .Select(i => i * dt)
            .ToArray();

        return Ok(new
        {
            paths = allPaths,
            timePoints
        });
    }


    [HttpGet("fx-forward-paths")]
    public IActionResult GetFxForwardPaths(
        [FromQuery] string model = "gbm",          // "gbm" or "ou"
        [FromQuery] int paths = 100,
        [FromQuery] int steps = 200,
        [FromQuery] double spot = 1.10,            // S0
        [FromQuery] double maturity = 1.0,         // T
        [FromQuery] double r_dom = 0.03,
        [FromQuery] double r_for = 0.01,
        // OU-specific
        [FromQuery] double kappa = 3.0,
        [FromQuery] double theta = 1.10,
        [FromQuery] double sigma_ou = 0.12,
        // GBM-specific vol (if model=gbm)
        [FromQuery] double sigma_gbm = 0.15)
    {
        var random = new Random();
        var dt = maturity / steps;
        var sqrtDt = Math.Sqrt(dt);

        var underlyingPaths = new List<List<double>>();
        var pvPaths = new List<List<double>>();

        double forwardAtT0 = spot * Math.Exp((r_dom - r_for) * maturity);
        double discountFactorAtT = Math.Exp(-r_dom * maturity);

        for (int p = 0; p < paths; p++)
        {
            var spotPath = new List<double> { spot };
            var pvPath   = new List<double> { 0.0 };   // PV at t=0 is 0

            double currentSpot = spot;

            for (int step = 0; step < steps; step++)
            {
                double z = NextGaussian(random);

                if (model == "ou")
                {
                    currentSpot += kappa * (theta - currentSpot) * dt + sigma_ou * sqrtDt * z;
                }
                else // gbm – risk-neutral drift
                {
                    double drift = (r_dom - r_for - 0.5 * sigma_gbm * sigma_gbm) * dt;
                    double diffusion = sigma_gbm * sqrtDt * z;
                    currentSpot *= Math.Exp(drift + diffusion);
                }

                spotPath.Add(currentSpot);

                // PV at this time step
                double timeLeft = maturity - (step + 1) * dt;
                double fwdAtThisTime = currentSpot * Math.Exp((r_dom - r_for) * timeLeft);
                double pv = Math.Exp(-r_dom * timeLeft) * (fwdAtThisTime - forwardAtT0);
                pvPath.Add(pv);
            }

            underlyingPaths.Add(spotPath);
            pvPaths.Add(pvPath);
        }

        var timePoints = Enumerable.Range(0, steps + 1)
            .Select(i => i * dt)
            .ToArray();

        // Helper to compute stats at each time step
        var ComputeStats = (List<List<double>> allPaths) =>
        {
            var stats = new List<object>();
            for (int step = 0; step <= steps; step++)
            {
                var valuesAtStep = allPaths.Select(path => path[step]).ToList();
                valuesAtStep.Sort(); // for percentile calculation

                double mean = valuesAtStep.Average();
                double p5  = valuesAtStep[(int)(0.05 * paths)];           // approx 5th percentile
                double p95 = valuesAtStep[(int)(0.95 * paths)];           // approx 95th percentile

                stats.Add(new
                {
                    time = timePoints[step],
                    mean,
                    p5,
                    p95
                });
            }
            return stats;
        };

        var underlyingStats = ComputeStats(underlyingPaths);
        var pvStats = ComputeStats(pvPaths);

        return Ok(new
        {
            underlyingStats,
            pvStats,
            timePoints,
            forwardPriceAtT0 = forwardAtT0,           // analytical forward
            initialPV = 0.0
        });
    }



    private static double NextGaussian(Random rng)
    {
        // Box-Muller transform (simple implementation)
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}