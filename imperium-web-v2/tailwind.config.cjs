
/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./index.html", "./src/**/*.{ts,tsx,js,jsx}"],
  theme: {
    extend: {
      colors: { parchment: "#FFF7E6", bronze: "#8C6239" },
      boxShadow: { soft: "0 10px 30px rgba(0,0,0,0.08)" }
    },
  },
  plugins: [],
};