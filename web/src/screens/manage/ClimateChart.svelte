<script lang="ts">
  import { niceTicks, runs, type HourPoint } from "../../lib/climate";

  // One measure over time: a line for the hourly average and, where given, a faint band from each hour's
  // lowest to highest. Drag or hover along it to read any hour. A gap means the sensor sent nothing then.
  let {
    title,
    points,
    since,
    until,
    format,
    colour,
  }: {
    title: string;
    points: HourPoint[];
    since: number;
    until: number;
    format: (value: number) => string;
    colour: string;
  } = $props();

  const height = 180;
  const margin = { top: 10, right: 10, bottom: 24, left: 40 };

  let width = $state(320);
  let picked = $state<HourPoint | null>(null);

  const values = $derived(
    points.flatMap((point) => [
      point.value,
      point.low ?? point.value,
      point.high ?? point.value,
    ]),
  );
  const ticks = $derived(
    values.length
      ? niceTicks(Math.min(...values), Math.max(...values))
      : [0, 1],
  );
  const plotWidth = $derived(Math.max(width - margin.left - margin.right, 10));
  const plotHeight = height - margin.top - margin.bottom;

  const x = (at: number) =>
    margin.left + ((at - since) / Math.max(until - since, 1)) * plotWidth;
  const y = (value: number) =>
    margin.top +
    plotHeight -
    ((value - ticks[0]) / (ticks[ticks.length - 1] - ticks[0])) * plotHeight;

  const lines = $derived(
    runs(points).map((run) =>
      run
        .map(
          (point, index) =>
            `${index ? "L" : "M"}${x(point.at).toFixed(1)},${y(point.value).toFixed(1)}`,
        )
        .join(""),
    ),
  );
  const bands = $derived(
    runs(points.filter((point) => point.low != null && point.high != null))
      .filter((run) => run.length > 1)
      .map(
        (run) =>
          run
            .map(
              (point, index) =>
                `${index ? "L" : "M"}${x(point.at).toFixed(1)},${y(point.high!).toFixed(1)}`,
            )
            .join("") +
          [...run]
            .reverse()
            .map(
              (point) =>
                `L${x(point.at).toFixed(1)},${y(point.low!).toFixed(1)}`,
            )
            .join("") +
          "Z",
      ),
  );
  const lone = $derived(
    runs(points)
      .filter((run) => run.length === 1)
      .map((run) => run[0]),
  );

  // A label at each midnight, thinned out over a long stretch so they never crowd.
  const days = $derived.by(() => {
    const start = new Date(since);
    const result: number[] = [];

    for (let offset = 0; ; offset++) {
      const day = new Date(
        start.getFullYear(),
        start.getMonth(),
        start.getDate() + offset,
      ).getTime();

      if (day > until) {
        break;
      }

      if (day >= since) {
        result.push(day);
      }
    }

    const every = Math.ceil(
      result.length / Math.max(Math.floor(plotWidth / 48), 1),
    );
    return result.filter((_, index) => index % every === 0);
  });

  const dayName = (at: number, long: boolean) =>
    new Date(at).toLocaleDateString(
      "en-GB",
      long ? { day: "numeric", month: "short" } : { weekday: "short" },
    );

  function pick(event: PointerEvent) {
    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    const at =
      since +
      ((event.clientX - box.left - margin.left) / plotWidth) * (until - since);
    picked = points.reduce<HourPoint | null>(
      (best, point) =>
        !best || Math.abs(point.at - at) < Math.abs(best.at - at)
          ? point
          : best,
      null,
    );
  }

  const when = (at: number) =>
    new Date(at).toLocaleString("en-GB", {
      weekday: "short",
      day: "numeric",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
    });
</script>

<figure class="climate-chart" bind:clientWidth={width}>
  <figcaption>{title}</figcaption>
  {#if points.length === 0}
    <p class="muted">No readings in this time.</p>
  {:else}
    <svg
      {width}
      {height}
      role="img"
      aria-label="{title}: from {format(Math.min(...values))} to {format(
        Math.max(...values),
      )}"
      style="--series: {colour}"
      onpointermove={pick}
      onpointerdown={pick}
      onpointerleave={() => (picked = null)}
    >
      {#each ticks as tick (tick)}
        <line
          class="grid"
          x1={margin.left}
          x2={width - margin.right}
          y1={y(tick)}
          y2={y(tick)}
        />
        <text class="tick" x={margin.left - 6} y={y(tick)} dy="0.32em"
          >{format(tick)}</text
        >
      {/each}
      {#each days as day (day)}
        <text class="tick day" x={x(day)} y={height - 6}
          >{dayName(day, until - since > 8 * 86_400_000)}</text
        >
      {/each}
      {#each bands as band, index (index)}<path class="band" d={band} />{/each}
      {#each lines as line, index (index)}<path
          class="series"
          d={line}
        />{/each}
      {#each lone as point (point.at)}
        <circle class="dot" cx={x(point.at)} cy={y(point.value)} r="3" />
      {/each}
      {#if picked}
        <line
          class="crosshair"
          x1={x(picked.at)}
          x2={x(picked.at)}
          y1={margin.top}
          y2={margin.top + plotHeight}
        />
        <circle
          class="dot ringed"
          cx={x(picked.at)}
          cy={y(picked.value)}
          r="4.5"
        />
      {/if}
    </svg>
    {#if picked}
      <p
        class="chart-tip"
        style="left: {Math.min(Math.max(x(picked.at), 70), width - 70)}px"
        role="status"
      >
        <span class="muted">{when(picked.at)}</span>
        <strong>{format(picked.value)}</strong>
        {#if picked.low != null && picked.high != null && picked.low !== picked.high}
          <span class="muted"
            >{format(picked.low)} to {format(picked.high)}</span
          >
        {/if}
      </p>
    {/if}
  {/if}
</figure>
