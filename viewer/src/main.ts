import { mount } from 'svelte';
import App from './App.svelte';
// The website's themes, controls and shell (web/src): the viewer is a page of the site and looks like one.
import '../../web/src/themes.css';
import '../../web/src/app.css';
import './app.css';
import { chooseTheme, loadTheme } from './lib/shell/theme';

// The theme before the first mount, as the website's theme-init.js does for its pages.
chooseTheme(loadTheme());

export default mount(App, { target: document.getElementById('app')! });
