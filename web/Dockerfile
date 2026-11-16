FROM node:22-alpine AS build
WORKDIR /app
COPY web/package*.json ./
RUN npm install --no-audit --no-fund
COPY web/ .
RUN npm run build
FROM nginx:1.27-alpine
COPY --from=build /app/dist/ /usr/share/nginx/html/
COPY infra/nginx/default.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 8080
