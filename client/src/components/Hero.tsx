import { useApp } from '../context/appState';

const copy = {
  Fall: { label: 'The autumn edition', title: 'The art of', accent: 'slowing down.', subtitle: 'Lantern-lit paths. Orchard afternoons. A little more time together.', image: '/images/autumn-editorial.jpg', caption: 'Golden hours, worth keeping.', detail: 'Woodland walks · Harvest gatherings · Fireside moments' },
  Winter: { label: 'The winter collection', title: 'Find your', accent: 'quiet wonder.', subtitle: 'Snowlight trails, candlelit music, and evenings that stay with you.', image: '/images/alpine-luxury.jpg', caption: 'A season apart. A moment together.', detail: 'Illuminated trails · Winter markets · Live performances' },
};

export function Hero() {
  const { state } = useApp();
  const text = copy[state.theme];
  return (
    <section className="hero" aria-labelledby="season-heading">
      <div className="hero-copy">
        <p className="eyebrow">Brushfire / {text.label}</p>
        <h1 id="season-heading">{text.title}<br /><em>{text.accent}</em></h1>
        <p className="hero-description">{text.subtitle}</p>
        <a className="hero-cta" href="#experiences">Explore the experiences <span aria-hidden="true">↗</span></a>
        <p className="hero-detail">{text.detail}</p>
      </div>
      <figure className="hero-visual">
        <img src={text.image} alt="" className="hero-art" width="1536" height="1024" fetchPriority="high" />
        <figcaption><span>01 / In season</span>{text.caption}</figcaption>
      </figure>
    </section>
  );
}
