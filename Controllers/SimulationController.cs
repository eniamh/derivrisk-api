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
        [FromQuery] string model = "gbm",
        [FromQuery] int paths = 100,
        [FromQuery] int steps = 200,
        [FromQuery] double spot = 1.10,
        [FromQuery] double maturity = 1.0,
        [FromQuery] double r_dom = 0.03,
        [FromQuery] double r_for = 0.01,
        // Model-specific
        [FromQuery] double kappa = 3.0,
        [FromQuery] double theta = 1.10,
        [FromQuery] double sigma_ou = 0.12,
        [FromQuery] double sigma_gbm = 0.15,
        // FX Forward parameters
        [FromQuery] double strike = 0,           // new – fixed K
        [FromQuery] double notional = 1_000_000, // new – amount in foreign
        [FromQuery] string direction = "buy")    // new: "buy" or "sell"
    {
        var random = new Random();
        var dt = maturity / steps;
        var sqrtDt = Math.Sqrt(dt);

        var underlyingPaths = new List<List<double>>();
        var pvPaths = new List<List<double>>();

        // Forward price at t=0 (used as strike if not provided)
        double forwardAtT0 = spot * Math.Exp((r_dom - r_for) * maturity);
        double finalStrike = strike > 0 ? strike : forwardAtT0;  // use provided strike or fair forward

        double sign = direction.ToLower() == "buy" ? 1.0 : -1.0;  // buy: +receive foreign, sell: -receive foreign

        for (int p = 0; p < paths; p++)
        {
            var spotPath = new List<double> { spot };
            var pvPath = new List<double> ();  

            double currentSpot = spot;

            // Calculate PV at t=0 (using shocked initial spot and fixed strike)
            double timeLeftAt0_0 = maturity;  // T - 0
            double fwdAtT0_0  = currentSpot * Math.Exp((r_dom - r_for) * timeLeftAt0_0 );
            double payoffAtMaturity_0  = sign * (fwdAtT0_0  - finalStrike) * notional;
            double pvAt0 = Math.Exp(-r_dom * timeLeftAt0_0 ) * payoffAtMaturity_0 ;
            pvPath.Add(pvAt0);

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

                // PV at this time step (fixed strike, scaled by notional & direction)
                double timeLeft = maturity - (step + 1) * dt;
                double fwdAtThisTime = currentSpot * Math.Exp((r_dom - r_for) * timeLeft);
                double payoffAtMaturity = sign * (fwdAtThisTime - finalStrike) * notional;
                double pv = Math.Exp(-r_dom * timeLeft) * payoffAtMaturity;
                pvPath.Add(pv);
            }

            underlyingPaths.Add(spotPath);
            pvPaths.Add(pvPath);
        }

        var timePoints = Enumerable.Range(0, steps + 1)
            .Select(i => i * dt)
            .ToArray();

        var underlyingStats = ComputeStats(underlyingPaths, timePoints); // add scenario later in frontend if needed
        var pvStats = ComputeStats(pvPaths, timePoints);

        return Ok(new
        {
            underlyingStats,
            pvStats,
            timePoints,
            forwardAtT0,
            underlyingPaths,
            usedStrike = strike > 0 ? strike : forwardAtT0,
            initialPV = 0.0
        });
    }

    // Helper method (add if missing)
    private List<object> ComputeStats(List<List<double>> paths, double[] times)
    {
        var stats = new List<object>();
        for (int step = 0; step < times.Length; step++)
        {
            var values = paths.Select(p => p[step]).ToList();
            values.Sort();
            double mean = values.Average();
            double p5  = values[(int)(0.05 * values.Count)];
            double p95 = values[(int)(0.95 * values.Count)];

            stats.Add(new
            {
                time = times[step],
                mean,
                p5,
                p95
            });
        }
        return stats;
    }

    private static double NextGaussian(Random rng)
    {
        // Box-Muller transform (simple implementation)
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}